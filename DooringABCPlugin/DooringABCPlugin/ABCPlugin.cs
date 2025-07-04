using Microsoft.Extensions.Logging;
using WMS5.Infrastructure.Attributes;
using WMS5.Infrastructure.Attributes.RestMethodAttribute;
using WMS5.Infrastructure.DataStructures.Request;
using WMS5.Infrastructure.DataStructures.Result;
using WMS5.Infrastructure.Helpers;
using WMS5.Infrastructure.Services;


namespace DooringABCPlugin;


[PluginClass("1.0.3")]
public class ABCPlugin
{
    //строка для подключения
    private string connectionString = "Host=192.168.200.13;Port=5432;Database=DatamartDocker2;Username=postgres;Password=postgres;";
    private double A = 0.85;
    private double B = 0.7;
    private int period = 7;

    [Autowire]
    public IPluginRepository PluginRepository { get; set; }

    [Autowire]
    public ILogger<ABCPlugin> Logger { get; set; }

    private void getParameters()
    {
        var args = PluginRepository.GetPluginConfig(nameof(ABCPlugin));
        string strA = "0.85";
        args.TryGetValue(nameof(A), out strA);
        double.TryParse(strA, out A);
        string strB = "0.7";
        args.TryGetValue(nameof(B), out strB);
        double.TryParse(strB, out B);
        string strperiod = "7";
        args.TryGetValue(nameof(period), out strperiod);
        int.TryParse(strperiod, out period);
        args.TryGetValue(nameof(connectionString), out connectionString);
        Logger.LogInformation($"Получены настройки: A: {A}, B: {B}, Period: {period}, ConnectionString: {connectionString}");
    }

    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "")]
    //атрибут определяет url для вызова метода, в данном случае http://{ip:port Датаменеджера}/GetABC
    [MethodPost("GetABC")]
    public RawRestResult GetABC(PostRequest<string> request)
    {
        internalGetABC();
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
        internalGetABC();
    }

    public void internalGetABC()
    {
        Logger.LogInformation($"{nameof(ABCPlugin)} started");
        getParameters();
        ABCQuery aBCQuery = new ABCQuery();
        List<ABC> list = aBCQuery.ReadDataBase(A, B, period, connectionString);
        aBCQuery.WriteDataBase(list, connectionString);
        aBCQuery.WriteToDictionary(list);
        Logger.LogInformation($"{nameof(ABCPlugin)} finished");
    }

}
