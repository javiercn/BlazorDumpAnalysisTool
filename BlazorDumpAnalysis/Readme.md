# Blazor server memory analyzer tool

Analyzes the memory of your dump file or process and lists statistics about the
circuits and components in it.

# Usage

```pre
Description:
  Analyze a Blazor Server app memory usage.

Usage:
  dotnet-blazor-server-memory-analyzer [options]

Options:
  -p, --process-id <process-id>  The ID of the process to analyze.
  -d, --dump <dump>              The memory dump file to analyze.
  --version                      Show version information
  -?, -h, --help                 Show help and usage information
```

# Sample output

# Circuits summary
|Total|Connected|Disconnected|
|-----|---------|------------|
|1|1|0|

|Circuit Id|Component Count|Circuit Generation|Disposed|
|----------|---------------|------------------|--------|
|Df5M3hTeYrloY6CeTfH-7eGY7OpLJjZp8rETC9A61q0|18|0|False|

### Circuit: Df5M3hTeYrloY6CeTfH-7eGY7OpLJjZp8rETC9A61q0
|Id|Name|Frames|Buffer|Size|
|--|----|------|------|----|
|0|Microsoft.AspNetCore.Components.Web.HeadOutlet|4|32|0.06 KB|
|1|Microsoft.AspNetCore.Components.Sections.SectionOutlet|3|32|0.83 KB|
|2|Microsoft.AspNetCore.Components.Sections.SectionOutlet|0|0|0.83 KB|
|3|BlazorServerMemoryDemo.App|4|32|0.05 KB|
|4|Microsoft.AspNetCore.Components.Routing.Router|7|32|1.37 KB|
|5|Microsoft.AspNetCore.Components.RouteView|3|32|0.18 KB|
|6|Microsoft.AspNetCore.Components.Routing.FocusOnNavigate|0|0|0.22 KB|
|7|Microsoft.AspNetCore.Components.LayoutView|2|32|0.05 KB|
|8|BlazorServerMemoryDemo.Shared.MainLayout|19|32|0.05 KB|
|9|Microsoft.AspNetCore.Components.Web.PageTitle|3|32|0.05 KB|
|10|BlazorServerMemoryDemo.Shared.NavMenu|45|64|0.05 KB|
|12|Microsoft.AspNetCore.Components.Sections.SectionContent|0|0|0.83 KB|
|13|Microsoft.AspNetCore.Components.Routing.NavLink|5|32|0.50 KB|
|14|Microsoft.AspNetCore.Components.Routing.NavLink|5|32|0.53 KB|
|15|Microsoft.AspNetCore.Components.Routing.NavLink|5|32|0.59 KB|
|20|Microsoft.AspNetCore.Components.Sections.SectionContent|0|0|0.83 KB|
|19|Microsoft.AspNetCore.Components.Web.PageTitle|3|32|0.05 KB|
|18|BlazorServerMemoryDemo.Pages.FetchData|120009|131072|391.07 KB|