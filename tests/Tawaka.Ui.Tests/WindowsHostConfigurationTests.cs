using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tawaka.Ui.Tests;

/// <summary>
/// The Windows host cannot be compiled here — the Linux SDK ships no Windows Desktop targets — so
/// the wiring that would fail at run time on a user's machine is checked against the files
/// instead.
/// <para>
/// This is not a substitute for running it (see docs/WINDOWS_VALIDATION.md). It is the guard for
/// the things that are silently wrong until somebody opens the application: a root component
/// selector that matches no element, a host page that is not there, a stylesheet path that
/// resolves to nothing, a window created before its services exist — and a solution file that does
/// not parse, which is what actually happened and went unnoticed for four milestones.
/// </para>
/// </summary>
public sealed class WindowsHostConfigurationTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Tawaka.Payroll.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    private static string Desktop(params string[] parts) =>
        Read(new[] { "src", "Tawaka.Ui.Desktop" }.Concat(parts).ToArray());

    // ---- The solution file -------------------------------------------------------------------

    /// <summary>
    /// The Windows solution failed to parse for four milestones, so no Windows build was possible
    /// at all. It was hand-edited because `dotnet sln add` refuses a WPF project on Linux, and the
    /// edit put configuration rows in a section that cannot hold them. This is the guard.
    /// </summary>
    [Fact]
    public void The_windows_solution_parses_and_carries_the_desktop_project()
    {
        var solution = Read("Tawaka.Payroll.sln");

        var desktop = Regex.Match(
            solution,
            @"Project\(""\{[^}]+\}""\) = ""Tawaka\.Ui\.Desktop"", ""([^""]+)"", ""\{([^}]+)\}""");

        Assert.True(desktop.Success, "Tawaka.Ui.Desktop is not in Tawaka.Payroll.sln.");

        var projectId = desktop.Groups[2].Value;

        // The configuration rows belong in ProjectConfigurationPlatforms, and nowhere else.
        var solutionConfigurations = Section(solution, "SolutionConfigurationPlatforms");
        foreach (var line in solutionConfigurations)
        {
            Assert.True(
                Regex.IsMatch(line, @"^[^=]+ = [^=]+$"),
                $"SolutionConfigurationPlatforms accepts only 'Name = Name' pairs, but holds: {line}");
        }

        var projectConfigurations = Section(solution, "ProjectConfigurationPlatforms");

        foreach (var configuration in new[] { "Debug|Any CPU", "Release|Any CPU" })
        {
            foreach (var role in new[] { "ActiveCfg", "Build.0" })
            {
                var expected = $"{{{projectId}}}.{configuration}.{role} = {configuration}";
                Assert.Contains(
                    expected,
                    projectConfigurations,
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        // No row may name two project GUIDs, and no Release row may map onto Debug.
        foreach (var line in projectConfigurations)
        {
            Assert.False(
                Regex.Matches(line, @"\{[0-9A-Fa-f-]{36}\}").Count > 1,
                $"A configuration row names more than one project: {line}");

            Assert.False(
                line.Contains("Release|Any CPU.", StringComparison.Ordinal) &&
                line.TrimEnd().EndsWith("Debug|Any CPU", StringComparison.Ordinal),
                $"A Release configuration is mapped onto Debug: {line}");
        }

        Assert.Contains(
            Section(solution, "NestedProjects"),
            line => line.Contains(projectId, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Section(string solution, string name)
    {
        var match = Regex.Match(
            solution,
            $@"GlobalSection\({name}\)[^\n]*\n(.*?)\tEndGlobalSection",
            RegexOptions.Singleline);

        Assert.True(match.Success, $"The solution has no {name} section.");

        return match.Groups[1].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    // ---- The host project --------------------------------------------------------------------

    [Fact]
    public void The_desktop_project_targets_windows_and_references_what_it_hosts()
    {
        var project = XDocument.Parse(Desktop("Tawaka.Ui.Desktop.csproj"));

        Assert.Equal("net8.0-windows", Value(project, "TargetFramework"));
        Assert.Equal("WinExe", Value(project, "OutputType"));
        Assert.Equal("true", Value(project, "UseWPF"));
        Assert.Equal("Tawaka.Payroll", Value(project, "AssemblyName"));

        // ApplicationHighDpiMode is a Windows Forms property and does nothing in WPF. Its presence
        // would read as configuration while having no effect.
        Assert.Null(Value(project, "ApplicationHighDpiMode"));

        var references = project.Descendants("ProjectReference")
            .Select(r => r.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        foreach (var expected in new[]
                 {
                     "Tawaka.Domain", "Tawaka.Application", "Tawaka.Payroll.Engine",
                     "Tawaka.Infrastructure", "Tawaka.Ui.Shared"
                 })
        {
            Assert.Contains(expected, references);
        }

        Assert.Contains(
            project.Descendants("PackageReference"),
            p => p.Attribute("Include")?.Value == "Microsoft.AspNetCore.Components.WebView.Wpf");
    }

    private static string? Value(XDocument project, string element) =>
        project.Descendants(element).FirstOrDefault()?.Value;

    /// <summary>
    /// The BlazorWebView binds `App.Services`, which is assigned during startup. A StartupUri
    /// would have WPF create the window before that happens, and the binding would resolve to
    /// null on a user's machine and nowhere else.
    /// </summary>
    [Fact]
    public void The_application_does_not_create_its_window_before_its_services_exist()
    {
        // The attribute, not the word: App.xaml explains in a comment why it has no StartupUri.
        var applicationXaml = Desktop("App.xaml");
        Assert.DoesNotContain(
            applicationXaml.Split('\n').Where(l => !l.TrimStart().StartsWith("<!--")),
            line => Regex.IsMatch(line, @"\bStartupUri\s*="));

        var startup = Desktop("App.xaml.cs");

        var servicesAssigned = startup.IndexOf("Services = services.BuildServiceProvider()",
            StringComparison.Ordinal);
        var windowCreated = startup.IndexOf("new MainWindow()", StringComparison.Ordinal);

        Assert.True(servicesAssigned > 0, "Startup never builds the service provider.");
        Assert.True(windowCreated > 0, "Startup never creates the main window.");
        Assert.True(
            servicesAssigned < windowCreated,
            "The main window is created before App.Services is assigned; the BlazorWebView would " +
            "bind a null service provider.");

        // Migrations and seeding both happen before the window opens, so a failure is a readable
        // message rather than a half-initialised screen.
        var migrated = startup.IndexOf("Database.Migrate()", StringComparison.Ordinal);
        var seeded = startup.IndexOf("ApplicationSeeder", StringComparison.Ordinal);

        Assert.True(migrated > 0 && migrated < windowCreated);
        Assert.True(seeded > 0 && seeded < windowCreated);
    }

    [Fact]
    public void The_root_component_selector_matches_an_element_on_the_host_page()
    {
        var window = Desktop("MainWindow.xaml");

        var hostPage = Regex.Match(window, @"HostPage=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(hostPage), "MainWindow.xaml names no HostPage.");

        var hostPath = Path.Combine(
            Root, "src", "Tawaka.Ui.Desktop", hostPage.Replace('\\', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(hostPath), $"The host page does not exist: {hostPage}");

        var html = File.ReadAllText(hostPath);

        var selector = Regex.Match(window, @"RootComponent Selector=""#([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(selector), "No RootComponent selector is declared.");
        Assert.Contains($"id=\"{selector}\"", html);

        // The component the selector is filled with has to be the one the application actually has.
        Assert.Contains("ComponentType=\"{x:Type shared:App}\"", window);
        Assert.Equal(
            "Tawaka.Ui.Shared.Components",
            typeof(Tawaka.Ui.Shared.Components.App).Namespace);

        Assert.Contains("clr-namespace:Tawaka.Ui.Shared.Components;assembly=Tawaka.Ui.Shared", window);
    }

    [Fact]
    public void The_host_page_loads_the_framework_script_and_a_stylesheet_that_exists()
    {
        var html = Desktop("wwwroot", "index.html");

        Assert.Contains("_framework/blazor.webview.js", html);
        Assert.Contains("<base href=\"/\" />", html);

        // A shared stylesheet is served from the Razor class library's static web assets. If the
        // file is not there, every screen renders unstyled and nothing says why.
        var stylesheet = Regex.Match(html, @"href=""_content/([^/]+)/([^""]+)""");
        Assert.True(stylesheet.Success, "The host page links no Razor class library stylesheet.");

        var assetPath = Path.Combine(
            Root, "src", stylesheet.Groups[1].Value, "wwwroot", stylesheet.Groups[2].Value);
        Assert.True(File.Exists(assetPath), $"The stylesheet does not exist: {assetPath}");

        // Blazor's error bar is hidden until the framework shows it. Without the rule it is
        // visible on every screen from the moment the application opens.
        Assert.Contains("id=\"blazor-error-ui\"", html);
        Assert.Contains("#blazor-error-ui { display: none; }", File.ReadAllText(assetPath));
    }

    [Fact]
    public void The_publish_profile_produces_a_self_contained_windows_application()
    {
        var profile = XDocument.Parse(
            Desktop("Properties", "PublishProfiles", "win-x64.pubxml"));

        Assert.Equal("win-x64", Value(profile, "RuntimeIdentifier"));
        Assert.Equal("true", Value(profile, "SelfContained"));
        Assert.Equal("true", Value(profile, "PublishSingleFile"));
        Assert.Equal("true", Value(profile, "IncludeNativeLibrariesForSelfExtract"));
        Assert.Equal("net8.0-windows", Value(profile, "TargetFramework"));

        // Trimming an application whose data layer builds its model by reflection fails at
        // start-up on the user's machine rather than in the build.
        Assert.Equal("false", Value(profile, "PublishTrimmed"));
    }
}
