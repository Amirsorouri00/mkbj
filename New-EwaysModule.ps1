#Requires -Version 5.1
<#
.SYNOPSIS
    Scaffolds a new module (folders + class library projects) for the EwaysVNext
    solution, mirroring the layout used by the "Catalog" / "Customer" modules, and
    wires the new projects into the solution's .slnx file.

.DESCRIPTION
    Given a module name, e.g. "Inventory", this script creates:

        src/Modules/Inventory/
        src/Modules/Inventory/Application/             -> EwaysVNext.Modules.Inventory.Application.csproj
        src/Modules/Inventory/Adapter/
        src/Modules/Inventory/Adapter/Outbound/         -> EwaysVNext.Modules.Inventory.Adapter.Outbound.csproj
        src/Modules/Inventory/Adapter/Inbound/
        src/Modules/Inventory/Adapter/Inbound/Rest/     -> EwaysVNext.Modules.Inventory.Adapter.Inbound.Rest.csproj
        src/Modules/Inventory/QueryApplication/         -> EwaysVNext.Modules.Inventory.QueryApplication.csproj
        src/Modules/Inventory/Installer/                -> EwaysVNext.Modules.Inventory.Installer.csproj

    Unless -SkipSlnx is passed, it also inserts the matching <Folder>/<Project> nodes
    into the .slnx file (by direct text insertion right after the
    "<Folder Name=\"/src/Modules/\" />" line - this does NOT depend on `dotnet sln`
    having .slnx support, since that varies by SDK version).

    Unless -SkipReferences is passed, it also wires the "usual" ProjectReferences
    between the new projects and the BuildingBlocks projects, based on the
    ports-and-adapters / CQRS naming convention visible in the solution
    (Adapter/Inbound = inbound port, Adapter/Outbound = outbound port,
    QueryApplication = read side). These are a reasonable *default* - since this
    script cannot see your actual code, review/adjust the $bb hashtable and the
    "Wiring project references" section below if your real convention differs.

.PARAMETER ModuleName
    PascalCase name of the new module, e.g. "Inventory", "Pricing", "Shipping".

.PARAMETER SolutionRoot
    Root folder of the solution (the folder that contains the .slnx file and "src\").
    Defaults to the current directory.

.PARAMETER SlnxPath
    Explicit path to the .slnx file. If omitted, the script looks for a single
    *.slnx file directly under -SolutionRoot.

.PARAMETER RootNamespacePrefix
    Prefix used for every generated project name. Defaults to "EwaysVNext" to match
    the existing projects (EwaysVNext.Modules.Catalog.Application, etc.).

.PARAMETER SkipSlnx
    Skip editing the .slnx file - only create folders/projects on disk.

.PARAMETER SkipReferences
    Skip wiring ProjectReferences between the generated projects / BuildingBlocks.

.PARAMETER MinimalCsproj
    Strip the TargetFramework/Nullable/ImplicitUsings boilerplate that
    "dotnet new classlib" adds, since Directory.Build.props already centralizes
    those settings. Defaults to $true.

.PARAMETER Force
    Continue even if the module folder already exists.

.EXAMPLE
    ./New-EwaysModule.ps1 -ModuleName Inventory

.EXAMPLE
    ./New-EwaysModule.ps1 -ModuleName Pricing -SolutionRoot C:\src\EwaysVNext -SkipReferences
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9]*$')]
    [string]$ModuleName,

    [string]$SolutionRoot = (Get-Location).Path,

    [string]$SlnxPath,

    [string]$RootNamespacePrefix = 'EwaysVNext',

    [switch]$SkipSlnx,

    [switch]$SkipReferences,

    [bool]$MinimalCsproj = $true,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Resolve-SlnxPath {
    param([string]$Root, [string]$Explicit)

    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "SlnxPath '$Explicit' not found." }
        return (Resolve-Path $Explicit).Path
    }

    $candidates = Get-ChildItem -Path $Root -Filter '*.slnx' -File
    if ($candidates.Count -eq 0) { throw "No .slnx file found under '$Root'. Pass -SlnxPath explicitly." }
    if ($candidates.Count -gt 1) { throw "Multiple .slnx files found under '$Root'. Pass -SlnxPath explicitly." }
    return $candidates[0].FullName
}

function Assert-Dotnet {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The 'dotnet' CLI was not found on PATH."
    }
}

function Invoke-Dotnet {
    param([string[]]$Arguments)
    Write-Host "  -> dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    # IMPORTANT: capture the native command's stdout into a variable. If it is left
    # unassigned, PowerShell treats it as this function's own return value, which
    # would silently corrupt every caller that does "$x = Invoke-Dotnet ..." or
    # "$created[$k] = New-ClassLibrary ..." (the dotnet output text gets glued onto
    # the real return value).
    $output = & dotnet @Arguments
    if ($output) { $output | ForEach-Object { Write-Host $_ } }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function New-ClassLibrary {
    param(
        [string]$ProjectName,
        [string]$FolderPath,
        [bool]$Minimal
    )

    New-Item -ItemType Directory -Path $FolderPath -Force | Out-Null

    Invoke-Dotnet -Arguments @('new', 'classlib', '-n', $ProjectName, '-o', $FolderPath, '--force')

    $csprojPath = Join-Path $FolderPath "$ProjectName.csproj"

    # Remove the generated sample class - we only want the empty project shell.
    $sampleClass = Join-Path $FolderPath 'Class1.cs'
    if (Test-Path $sampleClass) { Remove-Item $sampleClass -Force }

    if ($Minimal) {
        # Directory.Build.props already centralizes TargetFramework / Nullable /
        # ImplicitUsings, so strip the boilerplate "dotnet new classlib" adds and
        # rely on it instead of duplicating/overriding it per-project.
        $sdkMatch = Select-String -Path $csprojPath -Pattern '<Project Sdk="([^"]+)">' | Select-Object -First 1
        $sdkValue = if ($sdkMatch) { $sdkMatch.Matches[0].Groups[1].Value } else { 'Microsoft.NET.Sdk' }
        $minimalContent = "<Project Sdk=`"$sdkValue`">`r`n`r`n</Project>`r`n"
        Set-Content -Path $csprojPath -Value $minimalContent -NoNewline
    }

    return $csprojPath
}

function Add-ProjectReferenceSafe {
    param([string]$FromCsproj, [string]$ToCsproj, [string]$Label)

    if (-not (Test-Path $ToCsproj)) {
        Write-Warning "Reference target for '$Label' ('$ToCsproj') does not exist - skipping. Adjust the `$bb hashtable if your BuildingBlocks project names/paths differ."
        return
    }
    Invoke-Dotnet -Arguments @('add', $FromCsproj, 'reference', $ToCsproj)
}

function Get-RelativeSlnxPath {
    param([string]$FullPath, [string]$BasePath)
    $full = (Resolve-Path $FullPath).Path
    $rel = $full.Substring($BasePath.Length).TrimStart('\', '/')
    return ($rel -replace '\\', '/')
}

# ---------------------------------------------------------------------------

Assert-Dotnet

$SolutionRoot = (Resolve-Path $SolutionRoot).Path
$slnx = Resolve-SlnxPath -Root $SolutionRoot -Explicit $SlnxPath

$moduleRoot = Join-Path $SolutionRoot "src\Modules\$ModuleName"
if ((Test-Path $moduleRoot) -and -not $Force) {
    throw "Module folder '$moduleRoot' already exists. Pass -Force to continue anyway."
}

Write-Host "Solution root : $SolutionRoot"
Write-Host "Solution file : $slnx"
Write-Host "Module        : $ModuleName"
Write-Host ''

# Existing BuildingBlocks projects that the new module's projects will reference.
# Adjust these paths if your BuildingBlocks project names differ.
# The building blocks as they exist today. Framework, DDD, SharedKernel, Contracts and
# Infrastructure.Persistence were all removed in the Postgres rebuild; a module scaffolded
# against those names silently got no references at all, because Add-ProjectReferenceSafe only
# warns when a target is missing.
$bb = @{
    Domain              = Join-Path $SolutionRoot 'src\BuildingBlocks\Domain\EwaysVNext.Domain.csproj'
    Outcomes            = Join-Path $SolutionRoot 'src\BuildingBlocks\Outcomes\EwaysVNext.Outcomes.csproj'
    Persistence         = Join-Path $SolutionRoot 'src\BuildingBlocks\Persistence\EwaysVNext.Persistence.csproj'
    QueriesApplication  = Join-Path $SolutionRoot 'src\BuildingBlocks\Queries\Application\EwaysVNext.Queries.Application.csproj'
    EventsApplication   = Join-Path $SolutionRoot 'src\BuildingBlocks\Events\Application\EwaysVNext.Events.Application.csproj'
    CoreApplication     = Join-Path $SolutionRoot 'src\BuildingBlocks\Core\Application\EwaysVNext.Core.Application.csproj'
    PresentationEndpoint = Join-Path $SolutionRoot 'src\BuildingBlocks\Presentation.Endpoint\EwaysVNext.Presentation.Endpoint.csproj'
    DependencyInjection = Join-Path $SolutionRoot 'src\BuildingBlocks\DependencyInjection\EwaysVNext.DependencyInjection.csproj'
    Database            = Join-Path $SolutionRoot 'src\Database\EwaysVNext.Database.csproj'
}

# Definition of every project to create for the module, mirroring the Catalog module.
$projectDefs = @(
    [PSCustomObject]@{ Key = 'Application';       RelativeFolder = 'Application';         Suffix = 'Application' }
    [PSCustomObject]@{ Key = 'AdapterOutbound';    RelativeFolder = 'Adapter\Outbound';     Suffix = 'Adapter.Outbound' }
    [PSCustomObject]@{ Key = 'AdapterInboundRest'; RelativeFolder = 'Adapter\Inbound\Rest'; Suffix = 'Adapter.Inbound.Rest' }
    [PSCustomObject]@{ Key = 'QueryApplication';   RelativeFolder = 'QueryApplication';     Suffix = 'QueryApplication' }
    [PSCustomObject]@{ Key = 'Installer';          RelativeFolder = 'Installer';            Suffix = 'Installer' }
)

$created = @{}

foreach ($def in $projectDefs) {
    $projectName = "$RootNamespacePrefix.Modules.$ModuleName.$($def.Suffix)"
    $folderPath  = Join-Path $moduleRoot $def.RelativeFolder
    Write-Host "Creating $projectName" -ForegroundColor Cyan
    $created[$def.Key] = New-ClassLibrary -ProjectName $projectName -FolderPath $folderPath -Minimal $MinimalCsproj
}

# Make sure the "empty" intermediate folders exist too (Adapter\, Adapter\Inbound\),
# matching the explicit (project-less) folder nodes in the .slnx sample.
New-Item -ItemType Directory -Path (Join-Path $moduleRoot 'Adapter') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $moduleRoot 'Adapter\Inbound') -Force | Out-Null

if (-not $SkipReferences) {
    Write-Host ''
    Write-Host 'Wiring project references...' -ForegroundColor Cyan

    # Application: the hexagon. Domain primitives, the Result type, the unit of work and the
    # security principals - and deliberately NOT EF Core or any adapter (AGENTS.md rules 1 and 2,
    # enforced by Application_Depends_On_No_Adapter_And_No_EfCore).
    Add-ProjectReferenceSafe -FromCsproj $created.Application -ToCsproj $bb.Domain -Label 'Application -> Domain'
    Add-ProjectReferenceSafe -FromCsproj $created.Application -ToCsproj $bb.Outcomes -Label 'Application -> Outcomes'
    Add-ProjectReferenceSafe -FromCsproj $created.Application -ToCsproj $bb.Persistence -Label 'Application -> Persistence'
    Add-ProjectReferenceSafe -FromCsproj $created.Application -ToCsproj $bb.CoreApplication -Label 'Application -> Core.Application'
    Add-ProjectReferenceSafe -FromCsproj $created.Application -ToCsproj $bb.EventsApplication -Label 'Application -> Events.Application'

    # Outbound adapter: implements the Application's outbound ports against the one shared context.
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterOutbound -ToCsproj $created.Application -Label 'Adapter.Outbound -> Application'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterOutbound -ToCsproj $created.QueryApplication -Label 'Adapter.Outbound -> QueryApplication'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterOutbound -ToCsproj $bb.Persistence -Label 'Adapter.Outbound -> Persistence'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterOutbound -ToCsproj $bb.Database -Label 'Adapter.Outbound -> Database'

    # Inbound REST adapter: calls Application for writes and QueryApplication for reads.
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterInboundRest -ToCsproj $created.Application -Label 'Adapter.Inbound.Rest -> Application'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterInboundRest -ToCsproj $created.QueryApplication -Label 'Adapter.Inbound.Rest -> QueryApplication'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterInboundRest -ToCsproj $bb.PresentationEndpoint -Label 'Adapter.Inbound.Rest -> Presentation.Endpoint'
    Add-ProjectReferenceSafe -FromCsproj $created.AdapterInboundRest -ToCsproj $bb.Outcomes -Label 'Adapter.Inbound.Rest -> Outcomes'

    # QueryApplication: the read side, projecting straight from the shared context.
    Add-ProjectReferenceSafe -FromCsproj $created.QueryApplication -ToCsproj $bb.Outcomes -Label 'QueryApplication -> Outcomes'
    Add-ProjectReferenceSafe -FromCsproj $created.QueryApplication -ToCsproj $bb.QueriesApplication -Label 'QueryApplication -> Queries.Application'
    Add-ProjectReferenceSafe -FromCsproj $created.QueryApplication -ToCsproj $bb.Database -Label 'QueryApplication -> Database'

    # Installer (module composition root) -> everything it needs to register in DI
    Add-ProjectReferenceSafe -FromCsproj $created.Installer -ToCsproj $created.Application -Label 'Installer -> Application'
    Add-ProjectReferenceSafe -FromCsproj $created.Installer -ToCsproj $created.AdapterOutbound -Label 'Installer -> Adapter.Outbound'
    Add-ProjectReferenceSafe -FromCsproj $created.Installer -ToCsproj $created.AdapterInboundRest -Label 'Installer -> Adapter.Inbound.Rest'
    Add-ProjectReferenceSafe -FromCsproj $created.Installer -ToCsproj $created.QueryApplication -Label 'Installer -> QueryApplication'
    Add-ProjectReferenceSafe -FromCsproj $created.Installer -ToCsproj $bb.DependencyInjection -Label 'Installer -> DependencyInjection'
}
else {
    Write-Host 'Skipping reference wiring (-SkipReferences).' -ForegroundColor Yellow
}

if (-not $SkipSlnx) {
    Write-Host ''
    Write-Host "Updating $slnx ..." -ForegroundColor Cyan

    $relApp       = Get-RelativeSlnxPath -FullPath $created.Application -BasePath $SolutionRoot
    $relOutbound  = Get-RelativeSlnxPath -FullPath $created.AdapterOutbound -BasePath $SolutionRoot
    $relRest      = Get-RelativeSlnxPath -FullPath $created.AdapterInboundRest -BasePath $SolutionRoot
    $relQuery     = Get-RelativeSlnxPath -FullPath $created.QueryApplication -BasePath $SolutionRoot
    $relInstaller = Get-RelativeSlnxPath -FullPath $created.Installer -BasePath $SolutionRoot

    $block = @"
  <Folder Name="/src/Modules/$ModuleName/" />
  <Folder Name="/src/Modules/$ModuleName/Application/">
    <Project Path="$relApp" />
  </Folder>
  <Folder Name="/src/Modules/$ModuleName/Adapter/Outbound/">
    <Project Path="$relOutbound" />
  </Folder>
  <Folder Name="/src/Modules/$ModuleName/Adapter/" />
  <Folder Name="/src/Modules/$ModuleName/Adapter/Inbound/" />
  <Folder Name="/src/Modules/$ModuleName/Adapter/Inbound/Rest/">
    <Project Path="$relRest" />
  </Folder>
  <Folder Name="/src/Modules/$ModuleName/QueryApplication/">
    <Project Path="$relQuery" />
  </Folder>
  <Folder Name="/src/Modules/$ModuleName/Installer/">
    <Project Path="$relInstaller" />
  </Folder>
"@

    $slnxContent = Get-Content -Path $slnx -Raw

    if ($slnxContent -match [regex]::Escape("/src/Modules/$ModuleName/")) {
        Write-Warning "It looks like '$ModuleName' is already referenced in $slnx - skipping .slnx update to avoid duplicates. Add the block manually if needed."
    }
    else {
        $anchorPattern = '<Folder\s+Name="/src/Modules/"\s*/>'
        $match = [regex]::Match($slnxContent, $anchorPattern)
        if (-not $match.Success) {
            throw "Could not find anchor '<Folder Name=`"/src/Modules/`" />' in $slnx - please add the following block manually:`r`n$block"
        }

        $insertPos = $match.Index + $match.Length
        $newContent = $slnxContent.Substring(0, $insertPos) + "`r`n`r`n" + $block + $slnxContent.Substring($insertPos)
        Set-Content -Path $slnx -Value $newContent -NoNewline
        Write-Host '  -> inserted module block into .slnx' -ForegroundColor DarkGray
    }
}
else {
    Write-Host 'Skipping .slnx update (-SkipSlnx).' -ForegroundColor Yellow
}

Write-Host ''
Write-Host "Done. Created module '$ModuleName' under src\Modules\$ModuleName." -ForegroundColor Green
Write-Host "Run 'dotnet restore' (or reload the solution) to pick up the new projects." -ForegroundColor Green