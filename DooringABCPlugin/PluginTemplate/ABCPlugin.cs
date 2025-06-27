using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using WMS5.Core.Interfaces.Services;
using WMS5.Core.Interfaces.Services.Tasks;
using WMS5.CoreBase.Interfaces.Services;
using WMS5.Infrastructure.Attributes;
using WMS5.Infrastructure.Attributes.RestMethodAttribute;
using WMS5.Infrastructure.Helpers;
using WMS5.Infrastructure.Services;


namespace PluginsTemplates;


[PluginClass("1.0.0")]
public class ABCPlugin
{
    //строка для подключения
    private string connectionString = "Host=192.168.200.13;Port=5432;Database=DatamartDocker2;Username=postgres;Password=postgres;";
    private double A = 0.85;
    private double B = 0.7;
    private int period = 7;
    [Autowire]
    public IEntityManagerResolver EntityManager { get; set; }

    [Autowire]
    public ITaskManager TaskManager { get; set; }

    [Autowire]
    public ILockManager LockManager { get; set; }

    [Autowire]
    public IPluginRepository PluginRepository { get; set; }

    [Autowire]
    public ILogger<ABCPlugin> Logger { get; set; }

    [Autowire]
    public IDictionaryManagerResolver DictionaryManager { get; set; }

    [Autowire]
    public IProcessOperationLocationMovementTask LocationMovementClient { get; set; }

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
    }

    //атрибут обозначает, что данный метод будет доступен, как метод плагина
    //PluginConstants.UserWebAPI озночает, что данные метод будет вызываться через WebApi (например: Postman)
    [PluginMethod(PluginConstants.UserWebAPI, "GetABC", "")]
    //атрибут определяет url для вызова метода, в данном случае http://{ip:port Датаменеджера}/GetABC
    [MethodPost("GetABC")]
    public void GetABC()
    {
        internalGetABC();
    }

    //атрибут обозначает, что данный метод будет доступен, как метод плагина
    //PluginConstants.UserWebAPI озночает, что данные метод будет вызываться через WebApi (например: Postman)
    [PluginMethod]
    public void ReglamentGetABC()
    {
        internalGetABC();
    }

    public void internalGetABC()
    {
        getParameters();
        ABCQuery aBCQuery = new ABCQuery();
        List<ABC> list = aBCQuery.ReadDataBase(A, B, period, connectionString);
        aBCQuery.WriteDataBase(list, connectionString);
        aBCQuery.WriteToDictionary(list);
    }

}
