using System.Globalization;
using Microsoft.Extensions.Logging;
using Npgsql;
using WMS5.CoreBase.Extensions;
using WMS5.CoreBase.Interfaces.Services;
using WMS5.DataModel.Dictionaries.Storage;
using WMS5.DataModelBase.Base;

namespace DoringABCPlugin;

public class ABC
{
    public string commodityName;
    public double totalQuantity;
    public double quantityPercentage;
    public double cumulativePercentage;
    public string abcCategory;
    public string skuUuid;
    public string skuDomain;
    public DateTime periodStart;
    public DateOnly periodEnd;
}

public class ABCQuery
{
    public IDictionaryManagerResolver DictionaryManager { get; set; }

    public ILogger<ABCPlugin> Logger { get; set; }

    DateTime DateOfAnalysis;
    //запрос в бд для чтения
    string queryString = @"WITH 
                -- Все SKU из таблицы товаров с нужными полями
                all_skus AS (
                    SELECT 
                        s.""uuid"" AS sku_uuid,
                        s.""Name"" AS sku_name,
                        s.""uuid"" AS ox_uuid,
                        s.""domain"" AS sku_domain
                    FROM 
                        sku s
                    WHERE 
                        s.""domain"" = '{5}'
                ),-- Определяем дату начала периода (последние 7 дней)
                date_range AS (
                    SELECT 
                        CURRENT_DATE - INTERVAL '{0} days' AS start_date,
                        CURRENT_DATE AS end_date
                ),-- Товары, которые были отгружены (Shipped) за последнюю неделю
                shipped_skus AS (
                    SELECT DISTINCT
                        c.""SKU"" AS sku_uuid
                    FROM 
                        shippingorder so
                        JOIN shippingorder_lines sol ON so.""uuid"" = sol.""parentuuid""
                        JOIN Commodity c ON sol.""Commodity"" = c.""uuid""
                    WHERE 
                        so.""State"" = 'Shipped'
                        AND so.""ShippingDate"" >= (SELECT start_date FROM date_range)
                        AND so.""ShippingDate"" <= (SELECT end_date FROM date_range)
                ),-- Суммируем количество проданных товаров (только отгруженные за последнюю неделю)
                sales_data AS (
                    SELECT 
                        c.""SKU_name"" AS CommodityName,
                        c.""SKU"" AS sku_uuid,
                        SUM(sol.""QuantityPackage"") AS TotalQuantity
                    FROM 
                        shippingorder so
                        JOIN shippingorder_lines sol ON so.""uuid"" = sol.""parentuuid""
                        JOIN Commodity c ON sol.""Commodity"" = c.""uuid""
                    WHERE 
                        so.""State"" = 'Shipped'
                        AND so.""ShippingDate"" >= (SELECT start_date FROM date_range)
                        AND so.""ShippingDate"" <= (SELECT end_date FROM date_range)
                    GROUP BY 
                        c.""SKU_name"", c.""SKU""
                ),-- Объединяем все SKU с отметкой были ли они отгружены
                combined_data AS (
                    SELECT 
                        a.sku_name AS CommodityName,
                        a.ox_uuid AS ox_uuid,
                        a.sku_domain AS sku_domain,
                        COALESCE(sd.TotalQuantity, 0) AS TotalQuantity,
                        CASE WHEN ss.sku_uuid IS NOT NULL THEN 1 ELSE 0 END AS was_shipped
                    FROM 
                        all_skus a
                    LEFT JOIN 
                        shipped_skus ss ON a.sku_uuid = ss.sku_uuid
                    LEFT JOIN 
                        sales_data sd ON a.sku_uuid = sd.sku_uuid
                ),-- Вычисляем общую сумму для отгруженных товаров за период
                total_sales AS (
                    SELECT SUM(TotalQuantity) AS total
                    FROM combined_data
                    WHERE was_shipped = 1
                ),-- Рассчитываем доли и кумулятивную долю только для отгруженных товаров
                quantity_analysis AS (
                    SELECT 
                        cd.CommodityName,
                        cd.ox_uuid,
                        cd.sku_domain,
                        cd.TotalQuantity,
                        cd.was_shipped,
                        CASE 
                            WHEN cd.was_shipped = 1 AND ts.total > 0 THEN 
                                cd.TotalQuantity / ts.total
                            ELSE 0
                        END AS quantity_share,
                        CASE 
                            WHEN cd.was_shipped = 1 THEN 
                                SUM(cd.TotalQuantity) OVER (ORDER BY CASE WHEN cd.was_shipped = 1 THEN cd.TotalQuantity ELSE 0 END DESC) / ts.total
                            ELSE 0
                        END AS cumulative_quantity_share
                    FROM 
                        combined_data cd
                    CROSS JOIN 
                        total_sales ts
                )-- Классифицируем товары и выводим период анализа
                SELECT 
                    commodityname as ""CommodityName"",
                    ox_uuid AS ""SKUUUID"",
                    sku_domain AS ""SKUDomain"",
                    totalquantity as ""TotalQuantity"",
                    CASE 
                        WHEN was_shipped = 1 THEN quantity_share * 100 
                        ELSE 0 
                    END AS ""QuantityPercentage"",
                    CASE 
                        WHEN was_shipped = 1 THEN cumulative_quantity_share * 100 
                        ELSE 0 
                    END AS ""CumulativePercentage"",
                    CASE 
                        WHEN was_shipped = 0 THEN 'D' -- Товары, которые не были отгружены
                        WHEN cumulative_quantity_share <= {1} THEN 'A' -- 80% объема
                        WHEN cumulative_quantity_share <= {2} THEN 'B' -- 15% объема
                        ELSE 'C' -- 5% объема
                    END AS ""ABCCategory"",
                    (SELECT start_date FROM date_range) AS ""PeriodStart"",
                    (SELECT end_date FROM date_range) AS ""PeriodEnd""
                FROM 
                    quantity_analysis
                ORDER BY 
                    CASE 
                        WHEN was_shipped = 0 THEN 4 -- Категория D в конце
                        WHEN cumulative_quantity_share <= {3} THEN 1
                        WHEN cumulative_quantity_share <= {4} THEN 2
                        ELSE 3
                    END,
                    TotalQuantity DESC; ";

    //запрос для заполнения бд
    string insertString = @"insert into abc_analyze 
values (default, @CommodityName, @TotalQuantity, @QuantityPercentage, @CumulativePercentage, @ABCCategory, @SKUUUID, @SKUDomain, @PeriodStart, @PeriodEnd)";


    /// <summary>
    /// Метод для записи информации в справочник
    /// </summary>
    /// <param name="abc_analyzeRowsList">Список строк для записи</param>
    public void WriteToDictionary(List<ABC> abc_analyzeRowsList)
    {
        Logger.LogDebug($"WriteToDictionary(): start. Список содержит {abc_analyzeRowsList.Count} элементов");
        try
        {
            foreach (ABC abc_analyzeRow in abc_analyzeRowsList)
            {
                AbcClassification abc = (AbcClassification)(DictionaryManager.GetByCode(WMSType.GetMaster<AbcClassification>(), abc_analyzeRow.skuUuid));
                AbcClassification abcClone = new AbcClassification();
                if (abc != null)
                {
                    abc.CloneTo(abcClone);
                }
                else
                {
                    abcClone.UUID = Guid.NewGuid();
                    abcClone.Code = abc_analyzeRow.skuUuid;
                }
                abcClone.DateOfAnalysis = DateOfAnalysis;
                abcClone.AbcClass = abc_analyzeRow.abcCategory;
                abcClone.PeriodStart = abc_analyzeRow.periodStart;
                abcClone.PeriodEnd = abc_analyzeRow.periodEnd.ToDateTime(TimeOnly.MinValue);
                abcClone.Domain = Guid.Parse(abc_analyzeRow.skuDomain);
                abcClone.SKU = new DictionaryRef<SKU>(Guid.Parse(abc_analyzeRow.skuUuid));
                DictionaryManager.CreateOrUpdate(WMSType.GetMaster<AbcClassification>(), abcClone);
                
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error CreateOrUpdate");
        }
        Logger.LogDebug("WriteToDictionary(): end.");
    }

    /// <summary>
    /// Метод для записи информации в таблицу в БД
    /// </summary>
    /// <param name="abc_analyzeRowsList">Массив строк для записи в БД</param>
    /// <param name="connectionString">Строка подключения к БД</param>
    public void WriteDataBase(List<ABC> abc_analyzeRowsList, string connectionString)
    {
        Logger.LogDebug($"WriteDataBase(): start  Список содержит {abc_analyzeRowsList.Count} элементов");
        try
        {
            using (NpgsqlConnection conn = new NpgsqlConnection(connectionString))
            {
                foreach (ABC abc_analyzeRow in abc_analyzeRowsList)
                {
                    try
                    {
                        NpgsqlCommand cmd = new NpgsqlCommand(insertString, conn);
                        cmd.Parameters.AddWithValue("@CommodityName", abc_analyzeRow.commodityName);
                        cmd.Parameters.AddWithValue("@TotalQuantity", abc_analyzeRow.totalQuantity);
                        cmd.Parameters.AddWithValue("@QuantityPercentage", abc_analyzeRow.quantityPercentage);
                        cmd.Parameters.AddWithValue("@CumulativePercentage", abc_analyzeRow.cumulativePercentage);
                        cmd.Parameters.AddWithValue("@ABCCategory", abc_analyzeRow.abcCategory);
                        cmd.Parameters.AddWithValue("@SKUUUID", abc_analyzeRow.skuUuid);
                        cmd.Parameters.AddWithValue("@SKUDomain", abc_analyzeRow.skuDomain);
                        cmd.Parameters.AddWithValue("@PeriodStart", abc_analyzeRow.periodStart);
                        cmd.Parameters.AddWithValue("@PeriodEnd", abc_analyzeRow.periodEnd);
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error in function WriteDataBase()");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in function WriteDataBase()");
        }
        Logger.LogDebug("WriteDataBase(): end");

    }

    /// <summary>
    /// Метод для чтения таблицы, полученной sql-запросом
    /// </summary>
    /// <param name="A">Процент для А-категории</param>
    /// <param name="B">Процент от В-категории</param>
    /// <param name="period">Период, за который выполняется анализ (от сегодняшней даты)</param>
    /// <param name="connectionString">Строка для подключения к БД, к которой делается запрос</param>
    /// <param name="domainStr">Домен, в котором выполняется анализ</param>
    /// <returns>Возвращает массив, в котором содержатся все строки, полученные через запрос</returns>
    public List<ABC> ReadDataBase(double A, double B, int period, string connectionString, string domainStr)
    {
        Logger.LogDebug($"ReadDataBase(): start A = {A}, B = {B}, period = {period}, connection string = {connectionString}");

        List<ABC> abc_analyzeRowsList = new List<ABC>();
        DateOfAnalysis = DateTime.Now;
        try
        {
            using (NpgsqlConnection conn = new NpgsqlConnection(connectionString))
            {
                string preparedString = string.Format(queryString, period, A.ToString(CultureInfo.CreateSpecificCulture("en-GB")), B.ToString(CultureInfo.CreateSpecificCulture("en-GB")), A.ToString(CultureInfo.CreateSpecificCulture("en-GB")), B.ToString(CultureInfo.CreateSpecificCulture("en-GB")), domainStr);
                Logger.LogDebug($"Выполняю запрос {preparedString}");
                NpgsqlCommand cmd = new NpgsqlCommand(preparedString, conn);

                using (NpgsqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ABC abc = new ABC();
                        abc.commodityName = reader["CommodityName"].ToString();
                        abc.totalQuantity = Convert.ToDouble(reader["TotalQuantity"].ToString());
                        abc.quantityPercentage = Convert.ToDouble(reader["QuantityPercentage"].ToString());
                        abc.cumulativePercentage = Convert.ToDouble(reader["CumulativePercentage"].ToString());
                        abc.abcCategory = reader["ABCCategory"].ToString();
                        abc.skuUuid = reader["SKUUUID"].ToString();
                        abc.skuDomain = reader["SKUDomain"].ToString();
                        abc.periodStart = DateTime.Parse(reader["PeriodStart"].ToString());
                        abc.periodEnd = DateOnly.FromDateTime(DateTime.Parse(reader["PeriodEnd"].ToString()));
                        abc_analyzeRowsList.Add(abc);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in function ReadDataBase()");
        }
        Logger.LogDebug($"ReadDataBase(): end");
        return abc_analyzeRowsList;
    }
}