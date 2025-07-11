using Microsoft.Extensions.Logging;
using WMS5.CoreBase.Interfaces.Services;
using WMS5.DataModel.Dictionaries.Storage;
using WMS5.DataModelBase.Base;
using WMS5.Infrastructure.Attributes;
using WMS5.Infrastructure.Attributes.RestMethodAttribute;
using WMS5.Infrastructure.DataStructures.Request;
using WMS5.Infrastructure.DataStructures.Result;
using WMS5.Infrastructure.Definitions;
using WMS5.Infrastructure.Helpers;
using WMS5.Infrastructure.Services;


namespace DooringABCPlugin;


[PluginClass("1.0.57")]
[SpecificNodePlugin(ComponentTypes.DataManager)]
[SpecificNodePlugin(ComponentTypes.WHEventService)]
public class ABCPlugin
{
    //строка для подключения
    private string connectionString = "Host=192.168.200.13;Port=5432;Database=DataMartDocker2;Username=postgres;Password=postgres;";
    private double A = 0.85;
    private double B = 0.7;
    private int period = 7;
    private string domain;

    [Autowire]
    public IDictionaryManagerResolver DictionaryManager { get; set; }

    [Autowire]
    public IPluginRepository PluginRepository { get; set; }

    [Autowire]
    public ILogger<ABCPlugin> Logger { get; set; }

    //Получение настроек плагина
    private bool getParameters()
    {
        ExecutionContextHelper.DomainId = Guid.Parse(domain);
        Dictionary<string, string> args = PluginRepository.GetPluginConfig("ABCPlugin");

        if (args == null)
        {
            Logger.LogError("gatParameters(): не указаны настройки");
            return false;
        }
        if (args is not null)
        { 
            if (args.TryGetValue(nameof(A), out string strA) && double.TryParse(strA, out double AValue))
                A = AValue;
            if (args.TryGetValue(nameof(B), out string strB) && double.TryParse(strB, out double BValue))
                B = BValue;
            if (args.TryGetValue(nameof(period), out string strPeriod) && int.TryParse(strPeriod, out int periodValue))
                period = periodValue;
            if (args.TryGetValue(nameof(connectionString), out string connectionStringValue))
                connectionString = connectionStringValue;
        }
        Logger.LogInformation($"Получены настройки: domain: {domain} A: {A}, B: {B}, Period: {period}, ConnectionString: {connectionString}");
        return true;
    }

    //выполнение АВС-анализа через пост-запрос
    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "Выполнение ABC-анализа по post-запросу")]
    [MethodPost("GetABC")]
    public RawRestResult GetABC(PostRequest<string> request)
    {
        domain = request.Body;
        Logger.LogInformation("GetABC(): started");

        string resultString = InternalGetABC();

        RawRestResult result = new RawRestResult();
        result.ResultCode = 200;
        if (Logger == null) result.Body = "No Logger";
        else
            result.Body = resultString;

        Logger.LogInformation("GetABC(): finished");
        return result;
    }

    //вывод всей АВС-категории ОХ в логи
    [PluginMethod(PluginConstants.UserWebAPI, "PrintABC", "Вывод ABC-классификации в логи ABC")]
    [MethodPost("PrintABC")]
    public RawRestResult PrintABC(PostRequest<string> request)
    {
        var items = DictionaryManager.GetItems(WMSType.GetMaster<AbcClassification>());
        foreach (DictionaryItem? item in items)
        {
            AbcClassification abc = item as AbcClassification;
            Logger.LogInformation($"SKU: {abc.SKU.Item.Name}, ABC class: {abc.AbcClass}, Date Of Analysis: {abc.DateOfAnalysis}, Period Start/End: {abc.PeriodStart} / {abc.PeriodEnd}");
        }
        RawRestResult result = new RawRestResult();
        result.ResultCode = 200;
        result.Body = "OK";

        return result;
    }


    //регламентное выполнение АВС-анализа
    [PluginMethod(PluginConstants.RegularOperation, nameof(ReglamentGetABC), "Регламентное выполнение ABC анализа")]
    public void ReglamentGetABC()
    {
        Logger.LogInformation("ReglamentGetAbc(): started");
        string domain = ExecutionContextHelper.DomainId.ToString();
        Logger.LogInformation($"ReglamentGetAbc(): домен - {domain}");
        Logger.LogInformation($"{InternalGetABC()}");
        Logger.LogInformation("ReglamentGetAbc(): finished");
    }

    //выполнение АВС-анализа
    public string InternalGetABC()
    {
        Logger.LogInformation($"{nameof(ABCPlugin)} started");

        if (!getParameters()) return ("APCPlugin: finished");

        ABCQuery aBCQuery = new ABCQuery();
        aBCQuery.Logger = Logger;
        aBCQuery.DictionaryManager = DictionaryManager;

        List<ABC> list = aBCQuery.ReadDataBase(A, B, period, connectionString, domain);
        aBCQuery.WriteDataBase(list, connectionString);
        aBCQuery.WriteToDictionary(list);

        Logger.LogInformation($"{nameof(ABCPlugin)} finished");
        return ($"Обработано {list.Count} записей");
    }
}
