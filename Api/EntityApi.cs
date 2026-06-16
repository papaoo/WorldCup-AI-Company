using System.Net;

namespace PiPiClaw.Team;

/// <summary>
/// Entity read APIs for watch objects, employees, and assignments.
/// </summary>
public static class EntityApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/watch-objects" && req.HttpMethod == "GET")
        {
            var items = ApiHelpers.IncludeTestData(req) ? store.GetWatchObjects() : store.GetProductionWatchObjects();
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorldCupWatchObject);
            return true;
        }
        if (path == "/api/employees" && req.HttpMethod == "GET")
        {
            var items = ApiHelpers.IncludeTestData(req) ? store.GetEmployees() : store.GetProductionEmployees();
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorldCupEmployee);
            return true;
        }
        if (path == "/api/employee-assignments" && req.HttpMethod == "GET")
        {
            var items = ApiHelpers.IncludeTestData(req) ? store.GetAssignments() : store.GetProductionAssignments();
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListEmployeeAssignment);
            return true;
        }

        return false;
    }
}
