﻿using System;
using System.IO;
using System.Reflection;
using Chutzpah.Callbacks;
using Chutzpah.Models;
using Chutzpah.RunnerCallbacks;
using System.Linq;
using Chutzpah.Transformers;

namespace Chutzpah
{
    class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Console.WriteLine("Chutzpah console test runner ({0}-bit .NET {1})", IntPtr.Size * 8, Environment.Version);
            Console.WriteLine("Copyright (C) 2013 Matthew Manela (http://matthewmanela.com).");

            if (args.Length == 0 || args[0] == "/?")
            {
                PrintUsage();
                return -1;
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                CommandLine commandLine = CommandLine.Parse(args);
                SetGlobalOptions(commandLine);
                int failCount = RunTests(commandLine);

                if (commandLine.Wait)
                {
                    Console.WriteLine();
                    Console.Write("Press any key to continue...");
                    Console.ReadKey();
                    Console.WriteLine();
                }

                return failCount;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine();
                Console.WriteLine("error: {0}", ex.Message);
                return -1;
            }
        }

        private static void SetGlobalOptions(CommandLine commandLine)
        {
            GlobalOptions.Instance.CompilerCacheFile = commandLine.CompilerCacheFile;

            if (commandLine.CompilerCacheFileMaxSizeMb.HasValue)
            {
                GlobalOptions.Instance.CompilerCacheFileMaxSizeBytes = commandLine.CompilerCacheFileMaxSizeMb.Value * 1024 * 1024;
            }
        }


        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;

            if (ex != null)
                Console.WriteLine(ex.ToString());
            else
                Console.WriteLine("Error of unknown type thrown in applicaton domain");

            Environment.Exit(1);
        }

        static void PrintUsage()
        {
            string executableName = Path.GetFileNameWithoutExtension(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);

            Console.WriteLine();
            Console.WriteLine("usage: {0} [options]", executableName);
            Console.WriteLine("usage: {0} <testFile> [options]", executableName);
            Console.WriteLine();
            Console.WriteLine("Valid options:");
            Console.WriteLine("  /silent                : Do not output running test count");
            Console.WriteLine("  /teamcity              : Forces TeamCity mode (normally auto-detected)");
            Console.WriteLine("  /wait                  : Wait for input after completion");
            Console.WriteLine("  /failOnError           : Return a non-zero exit code if any script errors or timeouts occurs");
            Console.WriteLine("  /failOnScriptError     : Alias for failOnError (deprecated)");
            Console.WriteLine("  /debug                 : Print debugging information and tracing to console");
            Console.WriteLine("  /trace                 : Logs tracing information to chutzpah.log");
            Console.WriteLine("  /openInBrowser         : Launch the tests in the default browser");
            Console.WriteLine("  /timeoutMilliseconds   : Amount of time to wait for a test file to finish before failing. (Defaults to {0})", Constants.DefaultTestFileTimeout);
            Console.WriteLine("  /parallelism [n]       : Max degree of parallelism for Chutzpah. Defaults to number of CPUs + 1");
            Console.WriteLine("                         : If you specify more than 1 the test output may be a bit jumbled");
            Console.WriteLine("  /path path             : Adds a path to a folder or file to the list of test paths to run.");
            Console.WriteLine("                         : Specify more than one to add multiple paths.");
            Console.WriteLine("                         : If you give a folder, it will be scanned for testable files.");
            Console.WriteLine("                         : (e.g. /path test1.html /path testFolder)");
            Console.WriteLine("  /file path             : Alias for /path (deprecated)");
            Console.WriteLine("  /vsoutput              : Print output in a format that the VS error list recognizes");
            Console.WriteLine("  /coverage              : Enable coverage collection");
            Console.WriteLine("  /coverageIncludes pat  : Only instrument files that match the given shell patterns (in glob format), comma separated.");
            Console.WriteLine("  /coverageExcludes pat  : Don't instrument files that match the given shell pattern (in glob format), comma separated.");
            Console.WriteLine("  /compilercache         : File where compiled scripts can be cached. Defaults to a file in the temp directory.");
            Console.WriteLine("  /compilercachesize     : The maximum size of the cache in Mb. Defaults to 32Mb.");
            Console.WriteLine("  /testMode              : The mode to test in (All, Html, AllExceptHTML, TypeScript, CoffeeScript, JavaScript)");


            foreach (var transformer in SummaryTransformerFactory.GetTransformers())
            {
                Console.WriteLine("  /{0} filename        : {1}", transformer.Name, transformer.Description);
            }

            Console.WriteLine();
        }

        static int RunTests(CommandLine commandLine)
        {

            var testRunner = TestRunner.Create(debugEnabled: commandLine.Debug);

            if (commandLine.Trace)
            {
                ChutzpahTracer.AddFileListener();
            }

            var chutzpahAssemblyName = testRunner.GetType().Assembly.GetName();


            Console.WriteLine();
            Console.WriteLine("chutzpah.dll:     Version {0}", chutzpahAssemblyName.Version);
            Console.WriteLine();

            TestCaseSummary testResultsSummary = null;
            try
            {
                var callback = commandLine.TeamCity ? (ITestMethodRunnerCallback)new TeamCityConsoleRunnerCallback() : new StandardConsoleRunnerCallback(commandLine.Silent, commandLine.VsOutput);
                callback = new ParallelRunnerCallbackAdapter(callback);

                var testOptions = new TestOptions
                    {
                        OpenInBrowser = commandLine.OpenInBrowser,
                        TestFileTimeoutMilliseconds = commandLine.TimeOutMilliseconds,
                        MaxDegreeOfParallelism = commandLine.Parallelism,
                        TestingMode = commandLine.TestMode,
                        CoverageOptions = new CoverageOptions
                                              {
                                                  Enabled = commandLine.Coverage,
                                                  IncludePatterns = (commandLine.CoverageIncludePatterns ?? "").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries),
                                                  ExcludePatterns = (commandLine.CoverageExcludePatterns ?? "").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries)
                                              }
                    };

                if (!commandLine.Discovery)
                {
                    testResultsSummary = testRunner.RunTests(commandLine.Files, testOptions, callback);
                    ProcessTestSummaryTransformers(commandLine, testResultsSummary);
                }
                else
                {
                    Console.WriteLine("Test Discovery");
                    var tests = testRunner.DiscoverTests(commandLine.Files, testOptions).ToList();
                    Console.WriteLine("\nDiscovered {0} tests", tests.Count);

                    foreach (var test in tests)
                    {
                        Console.WriteLine("Test '{0}' from '{1}'", test.TestName, test.InputTestFile);
                    }
                    return 0;
                }

            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }

            var failedCount = testResultsSummary.FailedCount;
            if (commandLine.FailOnError && testResultsSummary.Errors.Any())
            {
                return failedCount > 0 ? failedCount : 1;
            }

            return failedCount;
        }

        private static void ProcessTestSummaryTransformers(CommandLine commandLine, TestCaseSummary testResultsSummary)
        {
            var transformers = SummaryTransformerFactory.GetTransformers();
            foreach (var transformer in transformers.Where(x => commandLine.UnmatchedArguments.ContainsKey(x.Name)))
            {
                var path = commandLine.UnmatchedArguments[transformer.Name];
                transformer.Transform(testResultsSummary, path);
            }
        }
    }
}