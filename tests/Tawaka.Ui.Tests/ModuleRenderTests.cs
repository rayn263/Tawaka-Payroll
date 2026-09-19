using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Ui.Shared.Components;

namespace Tawaka.Ui.Tests;

/// <summary>
/// Renders each module through the application's own router, exactly as a click in the navigation
/// menu would, and checks that the page produced its screen rather than throwing.
/// <para>
/// These tests exist because a Razor component that compiles can still fail on first render — the
/// SQLite ordering defect found in this QA pass took out every screen in the application and was
/// invisible to both the compiler and the service-level tests.
/// </para>
/// </summary>
public sealed class ModuleRenderTests : UiTestHost
{
    [Fact]
    public void Unauthenticated_root_shows_only_the_sign_in_screen()
    {
        var page = RenderComponent<App>();

        Assert.Contains("Tawaka Payroll", page.Markup);
        Assert.Contains("Sign in", page.Markup);
        Assert.DoesNotContain("Dashboard", page.Markup);
    }

    [Theory]
    [InlineData("", "Dashboard")]
    [InlineData("employees", "Employees")]
    [InlineData("employees/new", "Add employee")]
    [InlineData("payroll", "Payroll")]
    [InlineData("time-leave", "Time")]
    [InlineData("time-leave/leave", "Leave")]
    [InlineData("time-leave/calendar", "calendar")]
    [InlineData("time-leave/approvals", "approval")]
    [InlineData("loans", "Loans")]
    [InlineData("projects", "Projects")]
    [InlineData("statutory", "Statutory")]
    [InlineData("statutory/rules", "rule")]
    [InlineData("statutory/compliance", "Compliance")]
    [InlineData("reports", "Reports")]
    [InlineData("settings", "Settings")]
    [InlineData("administration", "Administration")]
    [InlineData("administration/audit", "Audit")]
    public void Every_module_renders_from_its_route(string route, string expected)
    {
        SignInWithEverything();
        GoTo(route);

        var page = RenderComponent<App>();

        Assert.Contains(expected, page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Page not found", page.Markup);
    }

    [Fact]
    public void An_unknown_route_shows_the_not_found_screen_rather_than_failing()
    {
        SignInWithEverything();
        GoTo("no-such-screen");

        var page = RenderComponent<App>();

        Assert.Contains("Page not found", page.Markup);
    }

    /// <summary>
    /// Navigation between modules has to work repeatedly in one session: the first render is the
    /// easy case, and a component that disposes badly fails on the second.
    /// </summary>
    [Fact]
    public void Navigating_between_every_module_in_one_session_works()
    {
        SignInWithEverything();
        var page = RenderComponent<App>();
        var nav = Services.GetRequiredService<NavigationManager>();

        foreach (var route in new[]
                 {
                     "employees", "payroll", "time-leave", "loans", "projects",
                     "statutory", "reports", "settings", "administration", "",
                     "employees", "reports", ""
                 })
        {
            nav.NavigateTo(route);
            Assert.DoesNotContain("Page not found", page.Markup);
        }
    }

    /// <summary>
    /// Nothing on any screen may be a placeholder, a leaked exception or a number that is not a
    /// number. These are the things that look like software and are not.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("employees")]
    [InlineData("employees/new")]
    [InlineData("payroll")]
    [InlineData("time-leave")]
    [InlineData("loans")]
    [InlineData("projects")]
    [InlineData("statutory")]
    [InlineData("statutory/rules")]
    [InlineData("statutory/compliance")]
    [InlineData("reports")]
    [InlineData("settings")]
    [InlineData("administration")]
    public void No_screen_shows_a_placeholder_a_leaked_exception_or_a_non_number(string route)
    {
        SignInWithEverything();
        GoTo(route);

        var markup = RenderComponent<App>().Markup;

        foreach (var forbidden in new[]
                 {
                     "NaN", "Infinity", "System.", "Exception", "StackTrace",
                     "TODO", "FIXME", "Lorem ipsum", "lorem ipsum",
                     "placeholder text", "Sample data", "sample value", "XXX",
                     "[object Object]", "undefined"
                 })
        {
            Assert.DoesNotContain(forbidden, markup, StringComparison.Ordinal);
        }

        // Blazor's error bar is present on every page and must stay hidden until something breaks.
        Assert.DoesNotContain("An unexpected error has occurred", markup);
    }

    private void GoTo(string route) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
}
