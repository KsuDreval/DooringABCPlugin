using Microsoft.Extensions.Logging;
using WMS5.CoreBase.Interfaces.Services;
using WMS5.Infrastructure.Attributes;
using WMS5.Infrastructure.Attributes.RestMethodAttribute;
using WMS5.Infrastructure.DataStructures.Request;
using WMS5.Infrastructure.DataStructures.Result;
using WMS5.Infrastructure.Definitions;
using WMS5.Infrastructure.Helpers;
using WMS5.Infrastructure.Services;


namespace DoringABCPlugin;

/// <summary>
/// Плагин для выполнения АВС-анализа и записи результатов в таблицу в БД и справочник
/// </summary>
/// <remarks>LOG-1190</remarks>
/// <author>k.dreval@logareon.ru</author>

[PluginClass("1.0.0")]
[SpecificNodePlugin(ComponentTypes.DataManager)]
[SpecificNodePlugin(ComponentTypes.WHEventService)]
public class ABCPlugin
{
    //строка для подключения
    private string _connectionString;
    private double _A = 0.8;
    private double _B = 0.95;
    private int _period;

    [Autowire]
    public IDictionaryManagerResolver DictionaryManager { get; set; }

    [Autowire]
    public IPluginRepository PluginRepository { get; set; }

    [Autowire]
    public ILogger<ABCPlugin> Logger { get; set; }

    /// <summary>
    /// Получение настроек плагина с UI
    /// </summary>
    /// <param name="domain">
    /// Домен для получения настроек
    /// </param>
    /// <returns>
    /// Возвращает False, если настройки не указаны или указаны неверно
    /// </returns>
    private bool GetParameters(string domain)
    {
        ExecutionContextHelper.DomainId = Guid.Parse(domain);
        Dictionary<string, string> args = PluginRepository.GetPluginConfig("ABCPlugin");

        if (args == null)
        {
            Logger.LogError("GetParameters(): не указаны настройки");
            return false;
        }
        else
        {
            if (args.TryGetValue("A", out string strA) && double.TryParse(strA, out double AValue))
                _A = AValue;
            if (args.TryGetValue("B", out string strB) && double.TryParse(strB, out double BValue))
                _B = BValue;
            if (args.TryGetValue("period", out string strPeriod) && int.TryParse(strPeriod, out int periodValue))
                _period = periodValue;
            else
            {
                Logger.LogError("GetParameters(): не удалось получить период");
                return false;
            }
            if (args.TryGetValue("connectionString", out string connectionStringValue))
                _connectionString = connectionStringValue;
            else
            {
                Logger.LogError("GetParameters(): не удалось получить строку подключения");
                return false;
            }
        }
        Logger.LogDebug($"Получены настройки: domain: {domain} A: {_A}, B: {_B}, Period: {_period}, ConnectionString: {_connectionString}");
        return true;
    }

    /// <summary>
    /// Вызов АВС-анализа через пост-запрос
    /// http://{ip-адрес сервера}:{порт ДМ-а}/GetABC
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "Выполнение ABC-анализа по post-запросу")]
    [MethodPost("GetABC")]
    public RawRestResult GetABC(PostRequest<string> request)
    {
        string domain = request.Body;
        Logger.LogInformation("GetABC(): started");

        string resultString = InternalGetABC(domain);

        RawRestResult result = new RawRestResult();
        result.ResultCode = 200;
        result.Body = resultString;

        Logger.LogInformation("GetABC(): finished");
        return result;
    }


    /// <summary>
    /// Вызов АВС-анализа через регламентную операцию
    /// </summary>
    [PluginMethod(PluginConstants.RegularOperation, nameof(ReglamentGetABC), "Регламентное выполнение ABC анализа")]
    public void ReglamentGetABC()
    {
        Logger.LogInformation("ReglamentGetAbc(): started");
        string domain = ExecutionContextHelper.DomainId.ToString();
        Logger.LogDebug($"ReglamentGetAbc(): домен - {domain}");
        string result = InternalGetABC(domain);
        Logger.LogInformation($"{result}");
        Logger.LogInformation("ReglamentGetAbc(): finished");
    }

    /// <summary>
    /// Метод вызывает методы класса ABCQuery для выполнения АВС-анализа и записи данных в таблицу БД и соответствующий справочник
    /// </summary>
    /// <param name="domain">Домен, в котором выполняется анализ</param>
    /// <returns>
    /// В случае успешного выполнение возвращает строку с количеством обработанных записей
    /// </returns>
    public string InternalGetABC(string domain)
    {
        Logger.LogInformation($"{nameof(ABCPlugin)} started");

        if (GetParameters(domain) == false) return ("APCPlugin: finished");

        ABCQuery aBCQuery = new ABCQuery();
        aBCQuery.Logger = Logger;
        aBCQuery.DictionaryManager = DictionaryManager;

        List<ABC> listABC = aBCQuery.ReadDataBase(_A, _B, _period, _connectionString, domain);
        aBCQuery.WriteDataBase(listABC, _connectionString);
        aBCQuery.WriteToDictionary(listABC);

        Logger.LogInformation($"{nameof(ABCPlugin)} finished");
        return ($"Обработано {listABC.Count} записей");
    }
}
