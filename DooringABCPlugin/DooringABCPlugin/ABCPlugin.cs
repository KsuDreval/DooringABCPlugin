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
using static WMS5.Infrastructure.DataStructures.Settings;


namespace DooringABCPlugin;


[PluginClass("1.0.30")]
[SpecificNodePlugin(ComponentTypes.DataManager)]
public class ABCPlugin
{
    //строка для подключения
    private string connectionString = "Host=192.168.200.13;Port=5432;Database=DataMartDocker2;Username=postgres;Password=postgres;";
    private double A = 0.85;
    private double B = 0.7;
    private int period = 7;

    [Autowire]
    public IDictionaryManagerResolver DictionaryManager { get; set; }

    [Autowire]
    public IPluginRepository PluginRepository { get; set; }

    [Autowire]
    public ILogger<ABCPlugin> Logger { get; set; }

    private void getParameters()
    {
        Dictionary<string, string> args = PluginRepository.GetPluginConfig(nameof(DooringABCPlugin));

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

        Logger.LogInformation($"Получены настройки: A: {A}, B: {B}, Period: {period}, ConnectionString: {connectionString}");
    }

    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "")]
    //атрибут определяет url для вызова метода, в данном случае http://{ip:port Датаменеджера}/GetABC
    [MethodPost("GetABC")]
    public RawRestResult GetABC(PostRequest<string> request)
    {
        Logger.LogInformation("Вызван GetABC");
        
        string resultString = InternalGetABC();

        RawRestResult result = new RawRestResult();
        result.ResultCode = 200;
        if (Logger == null) result.Body = "No Logger";
        else
            result.Body = resultString;

        return result;
    }

    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "")]
    //атрибут определяет url для вызова метода, в данном случае http://{ip:port Датаменеджера}/PrintABC
    [MethodPost("PrintABC")]
    public RawRestResult PrintABC(PostRequest<string> request)
    {
        var items = DictionaryManager.GetItems(WMSType.GetMaster<AbcClassification>());
        foreach (DictionaryItem? item in items)
        {
            AbcClassification abc = item as AbcClassification;
            Logger.LogInformation($"SKU: {abc.SKU.Item.Name}, ABC class: {abc.AbcClass}");

        }
        RawRestResult result = new RawRestResult();
        result.ResultCode = 200;
        result.Body = "OK";

        return result;
    }


    //атрибут обозначает, что данный метод будет доступен, как метод плагина
    //PluginConstants.UserWebAPI озночает, что данные метод будет вызываться через WebApi (например: Postman)
    [PluginMethod(PluginConstants.RegularOperation, nameof(ReglamentGetABC), "Регламентное выполнение ABC анализа")]
    public void ReglamentGetABC()
    {
        InternalGetABC();
    }

    public string InternalGetABC()
    {
        Logger.LogInformation($"{nameof(ABCPlugin)} started");
        getParameters();
        ABCQuery aBCQuery = new ABCQuery();
        aBCQuery.Logger = Logger;
        aBCQuery.DictionaryManager = DictionaryManager;
        List<ABC> list = aBCQuery.ReadDataBase(A, B, period, connectionString);
        aBCQuery.WriteDataBase(list, connectionString);
        aBCQuery.WriteToDictionary(list);
        Logger.LogInformation($"{nameof(ABCPlugin)} finished");
        return ($"Обработано {list.Count} записей");
    }

}
