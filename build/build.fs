module Build

open System
open Fake.Core
open Fake.DotNet
open Fake.Tools
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators
open Fake.Api
open Fake.BuildServer
open Argu

let environVarAsBoolOrDefault varName defaultValue =
    let truthyConsts = [
        "1"
        "Y"
        "YES"
        "T"
        "TRUE"
    ]

    try
        let envvar = (Environment.environVar varName).ToUpper()

        truthyConsts
        |> List.exists ((=) envvar)
    with _ ->
        defaultValue

//-----------------------------------------------------------------------------
// Metadata and Configuration
//-----------------------------------------------------------------------------

let rootDirectory =
    IO.Path.GetFullPath(
        __SOURCE_DIRECTORY__
        </> ".."
    )

let productName = "CozNet"

let sln =
    rootDirectory
    </> "CozNet.slnx"

let srcCodeGlob =
    !!(rootDirectory
       </> "src/**/*.fs")
    ++ (rootDirectory
        </> "src/**/*.fsx")
    -- (rootDirectory
        </> "src/**/obj/**/*.fs")

let testsCodeGlob =
    !!(rootDirectory
       </> "tests/**/*.fs")
    ++ (rootDirectory
        </> "tests/**/*.fsx")
    -- (rootDirectory
        </> "tests/**/obj/**/*.fs")

let srcGlob =
    rootDirectory
    </> "src/**/*.??proj"

let testsGlob =
    rootDirectory
    </> "tests/**/*.??proj"

let srcAndTest =
    !!srcGlob
    ++ testsGlob

let distDir =
    rootDirectory
    </> "dist"

let distGlob =
    distDir
    </> "*.nupkg"

let coverageThresholdPercent = 80

let coverageReportDir =
    rootDirectory
    </> "docs"
    </> "coverage"


let docsDir =
    rootDirectory
    </> "docs"

let docsSrcDir =
    rootDirectory
    </> "docsSrc"

let temp =
    rootDirectory
    </> "temp"

let watchDocsDir =
    temp
    </> "watch-docs"

let gitOwner = "TheAngryByrd"
let gitRepoName = "CozNet"

let gitHubRepoUrl = sprintf "https://github.com/%s/%s/" gitOwner gitRepoName

let documentationRootUrl = sprintf "https://%s.github.io/%s/" gitOwner gitRepoName

let releaseBranch = "master"
let readme = "README.md"
let changelogFile = "CHANGELOG.md"

let READMElink = Uri(Uri(gitHubRepoUrl), $"blob/{releaseBranch}/{readme}")
let CHANGELOGlink = Uri(Uri(gitHubRepoUrl), $"blob/{releaseBranch}/{changelogFile}")

let changelogPath =
    rootDirectory
    </> changelogFile


let changelog = Fake.Core.Changelog.load changelogPath

let mutable latestEntry =
    if Seq.isEmpty changelog.Entries then
        Changelog.ChangelogEntry.New("0.1.0", "0.1.0-alpha.1", Some DateTime.Today, None, [], false)
    else
        changelog.LatestEntry

let mutable changelogBackupFilename = ""

let publishUrl = "https://www.nuget.org"

let enableCodeCoverage = environVarAsBoolOrDefault "ENABLE_COVERAGE" false

let githubToken = Environment.environVarOrNone "GITHUB_TOKEN"

let nugetToken = Environment.environVarOrNone "NUGET_TOKEN"

//-----------------------------------------------------------------------------
// Helpers
//-----------------------------------------------------------------------------


let isRelease (targets: Target list) =
    targets
    |> Seq.map (fun t -> t.Name)
    |> Seq.exists ((=) "PublishToNuGet")

let invokeAsync f = async { f () }

let configuration (targets: Target list) =
    let defaultVal = if isRelease targets then "Release" else "Debug"

    match Environment.environVarOrDefault "CONFIGURATION" defaultVal with
    | "Debug" -> DotNet.BuildConfiguration.Debug
    | "Release" -> DotNet.BuildConfiguration.Release
    | config -> DotNet.BuildConfiguration.Custom config

let failOnBadExitAndPrint (p: ProcessResult) =
    if
        p.ExitCode
        <> 0
    then
        p.Errors
        |> Seq.iter Trace.traceError

        failwithf "failed with exitcode %d" p.ExitCode


let isCI = lazy environVarAsBoolOrDefault "CI" false

// CI Servers can have bizarre failures that have nothing to do with your code
let rec retryIfInCI times fn =
    match isCI.Value with
    | true ->
        if times > 1 then
            try
                fn ()
            with _ ->
                retryIfInCI (times - 1) fn
        else
            fn ()
    | _ -> fn ()

let failOnWrongBranch () =
    if
        Git.Information.getBranchName ""
        <> releaseBranch
    then
        failwithf "Not on %s.  If you want to release please switch to this branch." releaseBranch


module dotnet =
    let watch cmdParam program args =
        DotNet.exec cmdParam (sprintf "watch %s" program) args

    let run cmdParam args = DotNet.exec cmdParam "run" args

    let tool optionConfig command args =
        DotNet.exec optionConfig (sprintf "%s" command) args
        |> failOnBadExitAndPrint

    let reportgenerator optionConfig args =
        tool optionConfig "reportgenerator" args

    let fcswatch optionConfig args = tool optionConfig "fcswatch" args

    let fsharpAnalyzer optionConfig args =
        tool optionConfig "fsharp-analyzers" args

    let fantomas args = DotNet.exec id "fantomas" args

module FSharpAnalyzers =
    type Arguments =
        | Project of string
        | Analyzers_Path of string
        | Fail_On_Warnings of string list
        | Ignore_Files of string list
        | Verbose

        interface IArgParserTemplate with
            member s.Usage = ""


module DocsTool =
    let quoted s = $"\"%s{s}\""

    let fsDocsDotnetOptions (o: DotNet.Options) = {
        o with
            WorkingDirectory = rootDirectory
    }

    let fsDocsBuildParams configuration (p: Fsdocs.BuildCommandParams) = {
        p with
            Clean = Some true
            Input = Some(quoted docsSrcDir)
            Output = Some(quoted docsDir)
            Eval = Some true
            Projects = Some(Seq.map quoted (!!srcGlob))
            Properties = Some($"Configuration=%s{configuration}")
            Parameters =
                Some [
                    // https://fsprojects.github.io/FSharp.Formatting/content.html#Templates-and-Substitutions
                    "root", quoted documentationRootUrl
                    "fsdocs-collection-name", quoted productName
                    "fsdocs-repository-branch", quoted releaseBranch
                    "fsdocs-package-version", quoted latestEntry.NuGetVersion
                    "fsdocs-readme-link", quoted (READMElink.ToString())
                    "fsdocs-release-notes-link", quoted (CHANGELOGlink.ToString())
                ]
            Strict = Some true
    }


    let cleanDocsCache () = Fsdocs.cleanCache rootDirectory

    let build (configuration) =
        Fsdocs.build fsDocsDotnetOptions (fsDocsBuildParams configuration)


    let watch (configuration) =
        let buildParams bp =
            let bp =
                Option.defaultValue Fsdocs.BuildCommandParams.Default bp
                |> fsDocsBuildParams configuration

            {
                bp with
                    Output = Some watchDocsDir
                    Strict = None
            }

        Fsdocs.watch
            fsDocsDotnetOptions
            (fun p -> {
                p with
                    BuildCommandParams = Some(buildParams p.BuildCommandParams)
            })

let allReleaseChecks () =
    failOnWrongBranch ()
    Changelog.failOnEmptyChangelog latestEntry


let failOnLocalBuild () =
    if not isCI.Value then
        failwith "Not on CI. If you want to publish, please use CI."

let failOnCIBuild () =
    if isCI.Value then
        failwith "On CI. If you want to run this target, please use a local build."

let allPublishChecks () =
    failOnLocalBuild ()
    Changelog.failOnEmptyChangelog latestEntry

//-----------------------------------------------------------------------------
// Target Implementations
//-----------------------------------------------------------------------------

/// So we don't require always being on the latest MSBuild.StructuredLogger
let disableBinLog (p: MSBuild.CliArguments) = { p with DisableInternalBinLog = true }

let cleanDirectories directories =
    let root =
        rootDirectory.TrimEnd(IO.Path.DirectorySeparatorChar)
        + string IO.Path.DirectorySeparatorChar

    let comparison =
        if OperatingSystem.IsWindows() then
            StringComparison.OrdinalIgnoreCase
        else
            StringComparison.Ordinal

    let resolved =
        directories
        |> Seq.map (fun directory -> IO.Path.GetFullPath(directory, rootDirectory))
        |> Seq.toList

    for directory in resolved do
        if not (directory.StartsWith(root, comparison)) then
            failwithf "Refusing to clean a directory outside the repository: %s" directory

    Shell.cleanDirs resolved

let clean _ =
    [
        "bin"
        "temp"
        distDir
        coverageReportDir
    ]
    |> cleanDirectories

    !!srcGlob
    ++ testsGlob
    |> Seq.collect (fun p ->
        [
            "bin"
            "obj"
        ]
        |> Seq.map (fun sp ->
            IO.Path.GetDirectoryName p
            </> sp
        )
    )
    |> cleanDirectories

let dotnetRestore _ =
    [ sln ]
    |> Seq.map (fun dir ->
        fun () ->
            let args =
                []
                |> String.concat " "

            DotNet.restore
                (fun c -> {
                    c with
                        MSBuildParams = disableBinLog c.MSBuildParams
                        Common =
                            c.Common
                            |> DotNet.Options.withCustomParams (Some(args))
                })
                dir
    )
    |> Seq.iter (retryIfInCI 10)

let updateChangelog ctx =
    latestEntry <- Changelog.updateChangelog changelogPath changelog gitHubRepoUrl ctx

let revertChangelog _ =
    if String.isNotNullOrEmpty Changelog.changelogBackupFilename then
        Changelog.changelogBackupFilename
        |> Shell.copyFile changelogPath

let deleteChangelogBackupFile _ =
    if String.isNotNullOrEmpty Changelog.changelogBackupFilename then
        Shell.rm Changelog.changelogBackupFilename

let assemblyInfoMsBuildArgs () =
    let releaseChannel =
        match latestEntry.SemVer.PreRelease with
        | Some pr -> pr.Name
        | _ -> "release"

    let releaseDate = latestEntry.Date.Value.ToString("o")

    let gitHash =
        try
            Git.Information.getCurrentSHA1 (null)
        with _ ->
            ""

    [
        $"/p:Version={latestEntry.AssemblyVersion}"
        $"/p:AssemblyVersion={latestEntry.AssemblyVersion}"
        $"/p:FileVersion={latestEntry.AssemblyVersion}"
        $"/p:InformationalVersion={latestEntry.AssemblyVersion}"
        $"/p:AssemblyMetadataReleaseDate={releaseDate}"
        $"/p:AssemblyMetadataReleaseChannel={releaseChannel}"
        $"/p:AssemblyMetadataGitHash={gitHash}"
    ]


let dotnetBuild ctx =
    let args = [
        sprintf "/p:PackageVersion=%s" latestEntry.NuGetVersion
        "--no-restore"
    ]

    DotNet.build
        (fun c -> {
            c with
                MSBuildParams = disableBinLog c.MSBuildParams
                Configuration = configuration (ctx.Context.AllExecutingTargets)
                Common =
                    c.Common
                    |> DotNet.Options.withAdditionalArgs args

        })
        sln

let nativeScriptWithArgs script args =
    CreateProcess.fromRawCommand
        "pwsh"
        ([
            "-NoProfile"
            "-File"
            rootDirectory
            </> "native"
            </> script
         ]
         @ args)
    |> CreateProcess.withWorkingDirectory rootDirectory
    |> Proc.run
    |> fun result ->
        if
            result.ExitCode
            <> 0
        then
            failwithf "%s failed with exit code %d" script result.ExitCode

let nativeScript script = nativeScriptWithArgs script []

let nativeBuild _ = nativeScript "Build.ps1"

let nativeLinuxBuild _ = nativeScript "BuildLinux.ps1"

let nativeLinuxVerify _ = nativeScript "VerifyLinuxNative.ps1"

let serviceOwnerVerify platform _ =
    nativeScriptWithArgs "VerifyServiceOwner.ps1" [
        "-Platform"
        platform
    ]

let linuxSignalServiceVerify _ =
    nativeScriptWithArgs "VerifyServiceOwner.ps1" [
        "-Platform"
        "LinuxContainer"
        "-LinuxSignal"
    ]

let linuxServiceInteropVerify _ = nativeScript "VerifyServiceInterop.ps1"

let linuxSampledServiceVerify _ =
    nativeScript "VerifyLinuxSampledService.ps1"

let linuxSampledServicePhases _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-CapturePhases" ]

let linuxSampledServiceJit _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-CaptureJit" ]

let linuxInitializedServiceVerify _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-InitializeManagedPaths" ]

let linuxServiceSubmissionTiming _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-CaptureSubmission" ]

let linuxServiceSchedulerTiming _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-CaptureScheduler" ]

let linuxTargetLeaseDelivery _ =
    nativeScript "VerifyTargetLeaseDelivery.ps1"

let linuxTargetLeaseReuse _ =
    nativeScript "VerifyTargetLeaseReuse.ps1"

let linuxTargetLeaseCancellation _ =
    nativeScriptWithArgs "VerifyTargetLeaseReuse.ps1" [
        "-Mode"
        "cancel-before-delivery"
    ]

let linuxServiceOwnerPolling _ =
    nativeScriptWithArgs "VerifyLinuxSampledService.ps1" [ "-CompareOwnerPolling" ]

let linuxTargetLeaseArmRecovery _ =
    nativeScriptWithArgs "VerifyTargetLeaseReuse.ps1" [
        "-Mode"
        "reject-initial-arm"
    ]

let linuxTargetLeaseActiveCancellation _ =
    nativeScriptWithArgs "VerifyTargetLeaseReuse.ps1" [
        "-Mode"
        "cancel-active-handler"
    ]

let linuxTargetLeaseDisarmFailure _ =
    nativeScriptWithArgs "VerifyTargetLeaseReuse.ps1" [
        "-Mode"
        "disarm-failure"
    ]

let linuxTimerApi _ = nativeScript "VerifyTimerApi.ps1"

let linuxTimerInterop _ = nativeScript "VerifyTimerInterop.ps1"

let linuxSampledTimer _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [ "-SampledTimer" ]

let linuxSampledTimerPhases _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-ControllerPhases"
    ]

let linuxInitializedTimer _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-ControllerPhases"
        "-InitializeManagedPaths"
    ]

let linuxPreparedTimer _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-ControllerPhases"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
    ]

let linuxFastTimer _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-ControllerPhases"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
        "-ShortInitialInterval"
    ]

let linuxFastTimerCapacity _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
        "-ShortInitialInterval"
    ]

let linuxFastTimerScheduler _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
        "-ShortInitialInterval"
        "-SampledGc"
        "-SchedulerCapture"
    ]

let linuxTimerSchedulerCost _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
        "-ShortInitialInterval"
        "-SampledGc"
        "-SchedulerCost"
    ]

let linuxFastTimerGc _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-SampledTimer"
        "-InitializeManagedPaths"
        "-InitializeNativeTransport"
        "-ShortInitialInterval"
        "-SampledGc"
    ]

let linuxTimerManaged _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [ "-ManagedWorkload" ]

let linuxTimerManagedGc _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-ManagedWorkload"
        "-ForceGc"
    ]

let linuxTimerTransitions _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [ "-TransitionMatrix" ]

let linuxFastTimerTransitions _ =
    nativeScriptWithArgs "VerifyTimerInterop.ps1" [
        "-TransitionMatrix"
        "-ShortInitialInterval"
    ]

let timerLayoutVerify _ =
    CreateProcess.fromRawCommand "dotnet" [
        rootDirectory
        </> "tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll"
        "--verify-timer-layout"
    ]
    |> Proc.run
    |> fun result ->
        if
            result.ExitCode
            <> 0
        then
            failwith "Timer layout and receipt checks failed."

let linuxTargetLeaseDrainFailure _ =
    nativeScriptWithArgs "VerifyTargetLeaseReuse.ps1" [
        "-Mode"
        "drain-failure"
    ]

let linuxSelectedServiceInteropVerify _ =
    nativeScriptWithArgs "VerifyServiceInterop.ps1" [ "-SelectedOnly" ]

let linuxServiceSnapshotCost _ =
    nativeScriptWithArgs "VerifyServiceInterop.ps1" [ "-SnapshotCostOnly" ]

let serviceAccountingVerify _ =
    CreateProcess.fromRawCommand "dotnet" [
        rootDirectory
        </> "tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll"
        "--verify-service-accounting"
    ]
    |> Proc.run
    |> fun result ->
        if
            result.ExitCode
            <> 0
        then
            failwith "Service accounting checks failed."

let serviceAccountingCost _ =
    let directory =
        rootDirectory
        </> "artifacts/service-accounting-cost"

    System.IO.Directory.CreateDirectory directory
    |> ignore

    let output =
        directory
        </> (System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")
             + ".json")

    CreateProcess.fromRawCommand "dotnet" [
        rootDirectory
        </> "tests/CozNet.LinuxPerf/bin/Release/net10.0/CozNet.LinuxPerf.dll"
        "--service-accounting-cost"
        output
    ]
    |> Proc.run
    |> fun result ->
        if
            result.ExitCode
            <> 0
        then
            failwithf "Service accounting cost workload failed. Retain %s." output

let nativeWorkload _ =
    DotNet.build
        (fun options -> {
            options with
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = disableBinLog options.MSBuildParams
        })
        (rootDirectory
         </> "tests/CozNet.Instrumentation/CozNet.Instrumentation.csproj")

let nativeVerify _ = nativeScript "Verify.ps1"

let nativeMapWorkload _ =
    DotNet.build
        (fun options -> {
            options with
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = disableBinLog options.MSBuildParams
        })
        (rootDirectory
         </> "tests/CozNet.NativeMap/CozNet.NativeMap.csproj")

let nativeMapVerify platform _ =
    nativeScriptWithArgs "VerifyNativeMap.ps1" [
        "-Platform"
        platform
    ]

let nativeCountdownVerify script _ =
    for interval in
        [
            64
            256
            1024
        ] do
        nativeScriptWithArgs script [
            "-ProbeInterval"
            string interval
            "-EvidenceDirectory"
            rootDirectory
            </> "artifacts"
            </> "countdown"
            </> script
            </> string interval
        ]

let linuxPhysicalCalibration _ =
    nativeScript "VerifyPhysicalCalibration.ps1"

let linuxPerfWorkload _ =
    DotNet.build
        (fun options -> {
            options with
                Configuration = DotNet.BuildConfiguration.Release
                MSBuildParams = disableBinLog options.MSBuildParams
        })
        (rootDirectory
         </> "tests/CozNet.LinuxPerf/CozNet.LinuxPerf.csproj")

let linuxPerfVerify _ = nativeScript "VerifyLinuxPerf.ps1"

let linuxInjectedVerify _ = nativeScript "VerifyLinuxInjected.ps1"

let linuxCountdownVerify _ = nativeScript "VerifyLinuxCountdown.ps1"

let linuxOverheadVerify _ = nativeScript "VerifyLinuxOverhead.ps1"

let linuxSignalBuild _ = nativeScript "BuildLinuxSignal.ps1"

let linuxSignalVerify _ = nativeScript "VerifyLinuxSignal.ps1"

let linuxSignalWaitCompare _ =
    nativeScript "CompareLinuxSignalWaits.ps1"

let linuxSpinReliabilityVerify _ =
    nativeScript "VerifyLinuxSpinReliability.ps1"

let linuxRuntimeTraceCollect _ =
    nativeScript "CollectLinuxRuntimeTraces.ps1"

let linuxTargetCpuCollect _ =
    nativeScript "CollectLinuxTargetCpu.ps1"

let linuxTargetCpuMeasure _ =
    nativeScript "MeasureLinuxTargetCpu.ps1"

let linuxAbortTraceVerify _ =
    nativeScript "VerifyLinuxAbortTraces.ps1"

let linuxSchedulerVerify _ = nativeScript "VerifyLinuxScheduler.ps1"

let linuxSchedulerCollect _ =
    nativeScript "CollectLinuxScheduler.ps1"

let linuxSchedulerCost _ =
    nativeScript "MeasureLinuxSchedulerCost.ps1"

let linuxSchedulerTraceCollect _ =
    nativeScript "CollectLinuxSchedulerTraces.ps1"

let linuxSignalTransitionsVerify _ =
    nativeScript "VerifyLinuxSignalTransitions.ps1"

let linuxSampledSignalsVerify _ =
    nativeScript "VerifyLinuxSampledSignals.ps1"

let linuxSampleCadenceVerify _ =
    nativeScriptWithArgs "VerifyLinuxSampledSignals.ps1" [
        "-Cadence"
        "qualified-sample"
    ]

let linuxWarmSampleCadenceVerify _ =
    nativeScriptWithArgs "VerifyLinuxSampledSignals.ps1" [
        "-Cadence"
        "qualified-sample"
        "-Startup"
        "warm"
        "-Repetitions"
        "3"
    ]

let windowsOverheadVerify _ =
    nativeScript "VerifyWindowsOverhead.ps1"

let linuxPredictionVerify _ =
    nativeScript "VerifyLinuxPrediction.ps1"

let linuxCountdownPredictionVerify _ =
    nativeScriptWithArgs "VerifyLinuxPrediction.ps1" [
        "-ProbeMode"
        "injected"
        "-ProbeInterval"
        "1024"
        "-EvidenceDirectory"
        rootDirectory
        </> "artifacts"
        </> "linux-countdown-prediction"
    ]

let linuxAttributionVerify mode directory _ =
    nativeScriptWithArgs "VerifyLinuxPrediction.ps1" [
        "-ProbeMode"
        "injected"
        "-ProbeInterval"
        "1024"
        "-Attribution"
        mode
        "-EvidenceDirectory"
        rootDirectory
        </> "artifacts"
        </> directory
    ]

let linuxWaitCompare _ =
    for backend in
        [
            "managed"
            "linux-nanosleep"
        ] do
        for interval in
            [
                "0"
                "10"
            ] do
            nativeScriptWithArgs "VerifyLinuxPerf.ps1" [
                "-WaitBackend"
                backend
                "-ProbeCpuMilliseconds"
                interval
                "-EvidenceDirectory"
                rootDirectory
                </> "artifacts/linux-wait-comparison"
                </> (backend
                     + "-"
                     + interval)
            ]

let fsharpAnalyzers _ =
    let argParser =
        ArgumentParser.Create<FSharpAnalyzers.Arguments>(programName = "fsharp-analyzers")

    !!srcGlob
    |> Seq.iter (fun proj ->
        let args =
            [
                FSharpAnalyzers.Analyzers_Path(
                    rootDirectory
                    </> "packages/analyzers"
                )
                FSharpAnalyzers.Arguments.Project proj
                FSharpAnalyzers.Arguments.Fail_On_Warnings [ "BDH0002" ]
                FSharpAnalyzers.Arguments.Ignore_Files [ "*AssemblyInfo.fs" ]
                FSharpAnalyzers.Verbose
            ]
            |> argParser.PrintCommandLineArgumentsFlat

        dotnet.fsharpAnalyzer id args
    )

let dotnetTest ctx =
    let excludeCoverage =
        !!testsGlob
        |> Seq.map IO.Path.GetFileNameWithoutExtension
        |> String.concat "|"

    let isGenerateCoverageReport =
        ctx.Context.TryFindTarget("GenerateCoverageReport").IsSome

    let args = [
        "--no-build"
        if
            enableCodeCoverage
            || isGenerateCoverageReport
        then
            sprintf "/p:AltCover=true"

            if not isGenerateCoverageReport then
                sprintf "/p:AltCoverThreshold=%d" coverageThresholdPercent

            sprintf "/p:AltCoverAssemblyExcludeFilter=%s" excludeCoverage
            "/p:AltCoverLocalSource=true"
    ]

    DotNet.test
        (fun c ->

            {
                c with
                    MSBuildParams = disableBinLog c.MSBuildParams
                    Configuration = configuration (ctx.Context.AllExecutingTargets)
                    Logger = Some "trx;LogFilePrefix=testResults"
                    Common =
                        c.Common
                        |> DotNet.Options.withAdditionalArgs args
            })
        sln

let generateCoverageReport _ =
    let coverageReports =
        !!"tests/**/coverage*.xml"
        |> String.concat ";"

    let sourceDirs =
        !!srcGlob
        |> Seq.map Path.getDirectory
        |> String.concat ";"

    let independentArgs = [
        sprintf "-reports:\"%s\"" coverageReports
        sprintf "-targetdir:\"%s\"" coverageReportDir
        // Add source dir
        sprintf "-sourcedirs:\"%s\"" sourceDirs
        // Ignore Tests and if AltCover.Recorder.g sneaks in
        sprintf "-assemblyfilters:\"%s\"" "-*.Tests;-AltCover.Recorder.g"
        sprintf "-Reporttypes:%s" "Html"
    ]

    let args =
        independentArgs
        |> String.concat " "

    dotnet.reportgenerator id args

let showCoverageReport _ =
    failOnCIBuild ()

    coverageReportDir
    </> "index.html"
    |> Command.ShellCommand
    |> CreateProcess.fromCommand
    |> Proc.start
    |> ignore


let watchTests _ =
    !!testsGlob
    |> Seq.map (fun proj ->
        fun () ->
            dotnet.watch
                (fun opt ->
                    opt
                    |> DotNet.Options.withWorkingDirectory (IO.Path.GetDirectoryName proj)
                )
                "test"
                ""
            |> ignore
    )
    |> Seq.iter (
        invokeAsync
        >> Async.Catch
        >> Async.Ignore
        >> Async.Start
    )

    printfn "Press Ctrl+C (or Ctrl+Break) to stop..."

    let cancelEvent =
        Console.CancelKeyPress
        |> Async.AwaitEvent
        |> Async.RunSynchronously

    cancelEvent.Cancel <- true

let dotnetPack ctx =
    // Get release notes with properly-linked version number
    let releaseNotes = Changelog.mkReleaseNotes changelog latestEntry gitHubRepoUrl

    let args = [
        $"/p:PackageVersion={latestEntry.NuGetVersion}"
        yield! assemblyInfoMsBuildArgs ()
        $"/p:PackageReleaseNotes=\"{releaseNotes}\""
    ]

    DotNet.pack
        (fun c -> {
            c with
                MSBuildParams = disableBinLog c.MSBuildParams
                Configuration = configuration (ctx.Context.AllExecutingTargets)
                OutputPath = Some distDir
                Common =
                    c.Common
                    |> DotNet.Options.withAdditionalArgs args
        })
        sln

let publishToNuget _ =
    allReleaseChecks ()

    DotNet.nugetPush
        (fun c -> {
            c with
                Common = {
                    c.Common with
                        WorkingDirectory = distDir
                }
                PushParams = {
                    c.PushParams with
                        Source = Some publishUrl
                        ApiKey = nugetToken
                }
        })
        "*.nupkg"

    // If build fails after this point, we've pushed a release out with this version of CHANGELOG.md so we should keep it around
    Target.deactivateBuildFailure "RevertChangelog"

let gitRelease _ =
    allReleaseChecks ()

    let releaseNotesGitCommitFormat = latestEntry.ToString()

    Git.Staging.stageFile "" "CHANGELOG.md"
    |> ignore

    let msg =
        sprintf "Bump version to %s\n\n%s" latestEntry.NuGetVersion releaseNotesGitCommitFormat

    Git.Commit.exec "" msg

    Target.deactivateBuildFailure "RevertChangelog"

    Git.Branches.push ""

    let tag = Changelog.tagFromVersionNumber latestEntry.NuGetVersion

    Git.Branches.tag "" tag
    Git.Branches.pushTag "" "origin" tag

let githubRelease _ =
    allPublishChecks ()

    let token =
        match githubToken with
        | Some s -> s
        | _ ->
            failwith
                "please set the github_token environment variable to a github personal access token with repo access."

    let files = !!distGlob
    // Get release notes with properly-linked version number

    let releaseNotes = Changelog.mkReleaseNotes changelog latestEntry gitHubRepoUrl

    GitHub.createClientWithToken token
    |> GitHub.draftNewRelease
        gitOwner
        gitRepoName
        (Changelog.tagFromVersionNumber latestEntry.NuGetVersion)
        (latestEntry.SemVer.PreRelease
         <> None)
        (releaseNotes
         |> Seq.singleton)
    |> GitHub.uploadFiles files
    |> GitHub.publishDraft
    |> Async.RunSynchronously

let formatCode _ =
    let result = dotnet.fantomas $"{rootDirectory}"

    if not result.OK then
        printfn "Errors while formatting all files: %A" result.Messages

let checkFormatCode ctx =
    let result = dotnet.fantomas $"{rootDirectory} --check"

    if result.ExitCode = 0 then
        Trace.log "No files need formatting"
    elif result.ExitCode = 99 then
        failwith "Some files need formatting, check output for more info"
    else
        failwithf "Formatting failed with exit code %d: %A" result.ExitCode result.Errors


let cleanDocsCache _ = DocsTool.cleanDocsCache ()

let buildDocs ctx =
    let configuration = configuration (ctx.Context.AllExecutingTargets)
    DocsTool.build (string configuration)

let watchDocs ctx =
    let configuration = configuration (ctx.Context.AllExecutingTargets)
    DocsTool.watch (string configuration)


let initTargets () =
    BuildServer.install [ GitHubActions.Installer ]

    /// Defines a dependency - y is dependent on x. Finishes the chain.
    let (==>!) x y =
        x ==> y
        |> ignore

    /// Defines a soft dependency. x must run before y, if it is present, but y does not require x to be run. Finishes the chain.
    let (?=>!) x y =
        x ?=> y
        |> ignore
    //-----------------------------------------------------------------------------
    // Hide Secrets in Logger
    //-----------------------------------------------------------------------------
    Option.iter (TraceSecrets.register "<GITHUB_TOKEN>") githubToken
    Option.iter (TraceSecrets.register "<NUGET_TOKEN>") nugetToken
    //-----------------------------------------------------------------------------
    // Target Declaration
    //-----------------------------------------------------------------------------

    Target.create "Clean" clean
    Target.create "DotnetRestore" dotnetRestore
    Target.create "UpdateChangelog" updateChangelog
    Target.createBuildFailure "RevertChangelog" revertChangelog // Do NOT put this in the dependency chain
    Target.createFinal "DeleteChangelogBackupFile" deleteChangelogBackupFile // Do NOT put this in the dependency chain
    Target.create "DotnetBuild" dotnetBuild
    Target.create "NativeBuild" nativeBuild
    Target.create "NativeLinuxBuild" nativeLinuxBuild
    Target.create "NativeLinuxWorkload" nativeWorkload
    Target.create "NativeLinuxVerify" nativeLinuxVerify
    Target.create "ServiceOwnerVerify" (serviceOwnerVerify "Host")
    Target.create "LinuxServiceOwnerVerify" (serviceOwnerVerify "LinuxContainer")
    Target.create "LinuxSignalServiceVerify" linuxSignalServiceVerify
    Target.create "LinuxServiceInteropVerify" linuxServiceInteropVerify
    Target.create "LinuxSampledServiceVerify" linuxSampledServiceVerify
    Target.create "LinuxSampledServicePhases" linuxSampledServicePhases
    Target.create "LinuxSampledServiceJit" linuxSampledServiceJit
    Target.create "LinuxInitializedServiceVerify" linuxInitializedServiceVerify
    Target.create "LinuxServiceSubmissionTiming" linuxServiceSubmissionTiming
    Target.create "LinuxServiceSchedulerTiming" linuxServiceSchedulerTiming
    Target.create "LinuxServiceOwnerPolling" linuxServiceOwnerPolling
    Target.create "LinuxTargetLeaseDelivery" linuxTargetLeaseDelivery
    Target.create "LinuxTargetLeaseReuse" linuxTargetLeaseReuse
    Target.create "LinuxTargetLeaseCancellation" linuxTargetLeaseCancellation
    Target.create "LinuxTargetLeaseArmRecovery" linuxTargetLeaseArmRecovery
    Target.create "LinuxTargetLeaseActiveCancellation" linuxTargetLeaseActiveCancellation
    Target.create "LinuxTargetLeaseDisarmFailure" linuxTargetLeaseDisarmFailure
    Target.create "LinuxTargetLeaseDrainFailure" linuxTargetLeaseDrainFailure
    Target.create "LinuxTimerApi" linuxTimerApi
    Target.create "LinuxTimerInterop" linuxTimerInterop
    Target.create "LinuxTimerManaged" linuxTimerManaged
    Target.create "LinuxSampledTimer" linuxSampledTimer
    Target.create "LinuxSampledTimerPhases" linuxSampledTimerPhases
    Target.create "LinuxInitializedTimer" linuxInitializedTimer
    Target.create "LinuxPreparedTimer" linuxPreparedTimer
    Target.create "LinuxFastTimer" linuxFastTimer
    Target.create "LinuxFastTimerCapacity" linuxFastTimerCapacity
    Target.create "LinuxPhysicalCalibration" linuxPhysicalCalibration
    Target.create "LinuxFastTimerGc" linuxFastTimerGc
    Target.create "LinuxFastTimerScheduler" linuxFastTimerScheduler
    Target.create "LinuxTimerSchedulerCost" linuxTimerSchedulerCost
    Target.create "LinuxTimerManagedGc" linuxTimerManagedGc
    Target.create "LinuxTimerTransitions" linuxTimerTransitions
    Target.create "LinuxFastTimerTransitions" linuxFastTimerTransitions
    Target.create "TimerLayoutVerify" timerLayoutVerify
    Target.create "LinuxSelectedServiceInteropVerify" linuxSelectedServiceInteropVerify
    Target.create "LinuxServiceSnapshotCost" linuxServiceSnapshotCost
    Target.create "ServiceAccountingVerify" serviceAccountingVerify
    Target.create "ServiceAccountingCost" serviceAccountingCost
    Target.create "NativeWorkload" nativeWorkload
    Target.create "NativeVerify" nativeVerify
    Target.create "NativeMapWorkload" nativeMapWorkload
    Target.create "NativeLinuxMapWorkload" nativeMapWorkload
    Target.create "NativeMapVerify" (nativeMapVerify "windows")
    Target.create "NativeLinuxMapVerify" (nativeMapVerify "linux")
    Target.create "NativeCountdownVerify" (nativeCountdownVerify "Verify.ps1")
    Target.create "NativeLinuxCountdownVerify" (nativeCountdownVerify "VerifyLinuxNative.ps1")
    Target.create "LinuxPerfWorkload" linuxPerfWorkload
    Target.create "LinuxPerfVerify" linuxPerfVerify
    Target.create "LinuxInjectedWorkload" linuxPerfWorkload
    Target.create "LinuxInjectedVerify" linuxInjectedVerify
    Target.create "LinuxCountdownVerify" linuxCountdownVerify
    Target.create "LinuxOverheadVerify" linuxOverheadVerify
    Target.create "LinuxSignalBuild" linuxSignalBuild
    Target.create "LinuxSignalWorkload" linuxPerfWorkload
    Target.create "LinuxSignalVerify" linuxSignalVerify
    Target.create "LinuxSignalWaitCompare" linuxSignalWaitCompare
    Target.create "LinuxSpinReliabilityVerify" linuxSpinReliabilityVerify
    Target.create "LinuxRuntimeTraceCollect" linuxRuntimeTraceCollect
    Target.create "LinuxTargetCpuCollect" linuxTargetCpuCollect
    Target.create "LinuxTargetCpuMeasure" linuxTargetCpuMeasure
    Target.create "LinuxAbortTraceVerify" linuxAbortTraceVerify
    Target.create "LinuxSchedulerVerify" linuxSchedulerVerify
    Target.create "LinuxSchedulerCollect" linuxSchedulerCollect
    Target.create "LinuxSchedulerCost" linuxSchedulerCost
    Target.create "LinuxSchedulerTraceCollect" linuxSchedulerTraceCollect
    Target.create "LinuxSignalTransitionsVerify" linuxSignalTransitionsVerify
    Target.create "LinuxSampledSignalsVerify" linuxSampledSignalsVerify
    Target.create "LinuxSampleCadenceVerify" linuxSampleCadenceVerify
    Target.create "LinuxWarmSampleCadenceVerify" linuxWarmSampleCadenceVerify
    Target.create "WindowsOverheadWorkload" linuxPerfWorkload
    Target.create "WindowsOverheadVerify" windowsOverheadVerify
    Target.create "LinuxCountdownPredictionVerify" linuxCountdownPredictionVerify

    Target.create
        "LinuxRawAttributionVerify"
        (linuxAttributionVerify "raw-whole-method" "linux-raw-attribution")

    Target.create
        "LinuxFilteredAttributionVerify"
        (linuxAttributionVerify "raw-exclude-candidates" "linux-filtered-attribution")

    Target.create "LinuxWaitCompare" linuxWaitCompare
    Target.create "LinuxPredictionVerify" linuxPredictionVerify
    Target.create "FSharpAnalyzers" fsharpAnalyzers
    Target.create "DotnetTest" dotnetTest
    Target.create "GenerateCoverageReport" generateCoverageReport
    Target.create "ShowCoverageReport" showCoverageReport
    Target.create "WatchTests" watchTests
    Target.create "DotnetPack" dotnetPack
    Target.create "PublishToNuGet" publishToNuget
    Target.create "GitRelease" gitRelease
    Target.create "GitHubRelease" githubRelease
    Target.create "FormatCode" formatCode
    Target.create "CheckFormatCode" checkFormatCode
    Target.create "Release" ignore // For local
    Target.create "Publish" ignore //For CI
    Target.create "CleanDocsCache" cleanDocsCache
    Target.create "BuildDocs" buildDocs
    Target.create "WatchDocs" watchDocs

    "LinuxPerfWorkload"
    ==> "LinuxPerfVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxServiceInteropVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxTimerInterop"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSampledTimer"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSampledTimerPhases"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxInitializedTimer"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxPreparedTimer"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxFastTimer"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxFastTimerCapacity"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxPhysicalCalibration"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxFastTimerGc"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxFastTimerScheduler"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxTimerSchedulerCost"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxFastTimerTransitions"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxTimerManaged"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxTimerManagedGc"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxTimerTransitions"
    |> ignore

    "LinuxPerfWorkload"
    ==> "TimerLayoutVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSampledServiceVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSampledServicePhases"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSampledServiceJit"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxInitializedServiceVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxServiceSubmissionTiming"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxServiceSchedulerTiming"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxServiceOwnerPolling"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxSelectedServiceInteropVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxServiceSnapshotCost"
    |> ignore

    "LinuxPerfWorkload"
    ==> "ServiceAccountingVerify"
    |> ignore

    "LinuxPerfWorkload"
    ==> "ServiceAccountingCost"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxWaitCompare"
    |> ignore

    "LinuxPerfWorkload"
    ==> "LinuxPredictionVerify"
    |> ignore

    "NativeBuild"
    ==> "NativeWorkload"
    ==> "NativeVerify"
    |> ignore

    "NativeBuild"
    ==> "NativeMapWorkload"
    ==> "NativeMapVerify"
    |> ignore

    "NativeLinuxBuild"
    ==> "NativeLinuxMapWorkload"
    ==> "NativeLinuxMapVerify"
    |> ignore

    "NativeWorkload"
    ==> "NativeCountdownVerify"
    |> ignore

    "NativeLinuxBuild"
    ==> "NativeLinuxWorkload"
    ==> "NativeLinuxVerify"
    |> ignore

    "NativeLinuxWorkload"
    ==> "NativeLinuxCountdownVerify"
    |> ignore

    "NativeLinuxBuild"
    ==> "LinuxInjectedWorkload"
    ==> "LinuxInjectedVerify"
    |> ignore

    "LinuxInjectedWorkload"
    ==> "LinuxCountdownVerify"
    |> ignore

    "LinuxInjectedWorkload"
    ==> "LinuxOverheadVerify"
    |> ignore

    "LinuxSignalBuild"
    ==> "LinuxSignalWorkload"
    ==> "LinuxSignalVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSignalTransitionsVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSampledSignalsVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSampleCadenceVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxWarmSampleCadenceVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSignalWaitCompare"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSpinReliabilityVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxRuntimeTraceCollect"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxTargetCpuCollect"
    |> ignore

    "LinuxSignalBuild"
    ==> "LinuxTargetCpuMeasure"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxAbortTraceVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSchedulerVerify"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSchedulerCollect"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSchedulerCost"
    |> ignore

    "LinuxSignalWorkload"
    ==> "LinuxSchedulerTraceCollect"
    |> ignore

    "NativeBuild"
    ==> "WindowsOverheadWorkload"
    ==> "WindowsOverheadVerify"
    |> ignore

    "LinuxInjectedWorkload"
    ==> "LinuxCountdownPredictionVerify"
    |> ignore

    "LinuxInjectedWorkload"
    ==> "LinuxRawAttributionVerify"
    |> ignore

    "LinuxInjectedWorkload"
    ==> "LinuxFilteredAttributionVerify"
    |> ignore

    //-----------------------------------------------------------------------------
    // Target Dependencies
    //-----------------------------------------------------------------------------


    // Only call Clean if DotnetPack was in the call chain
    // Ensure Clean is called before DotnetRestore
    "Clean"
    ?=>! "DotnetRestore"

    "Clean"
    ==>! "DotnetPack"

    // Ensure UpdateChangelog is called after DotnetRestore
    "DotnetRestore"
    ?=>! "UpdateChangelog"

    "CleanDocsCache"
    ==>! "BuildDocs"

    "DotnetBuild"
    ?=>! "BuildDocs"

    "DotnetBuild"
    ==>! "BuildDocs"


    "DotnetBuild"
    ==>! "WatchDocs"

    "DotnetTest"
    ==> "GenerateCoverageReport"
    ==>! "ShowCoverageReport"

    "UpdateChangelog"
    ==> "GitRelease"
    ==>! "Release"


    "DotnetRestore"
    =?> ("CheckFormatCode", isCI.Value)
    ==> "DotnetBuild"
    ==> "DotnetTest"
    ==> "DotnetPack"
    ==> "PublishToNuGet"
    ==> "GitHubRelease"
    ==>! "Publish"

    "DotnetRestore"
    ==>! "WatchTests"

//-----------------------------------------------------------------------------
// Target Start
//-----------------------------------------------------------------------------
[<EntryPoint>]
let main argv =
    argv
    |> Array.toList
    |> Context.FakeExecutionContext.Create false "build.fsx"
    |> Context.RuntimeContext.Fake
    |> Context.setExecutionContext

    initTargets ()
    Target.runOrDefaultWithArguments "DotnetPack"

    0 // return an integer exit code
