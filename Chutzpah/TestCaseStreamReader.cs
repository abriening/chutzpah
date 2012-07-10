﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chutzpah.Models;
using Chutzpah.Models.JS;
using Chutzpah.Wrappers;

namespace Chutzpah
{
    /// <summary>
    /// Reads from the stream of test results writen by our phantom test runner. As events from this stream arrive we 
    /// will derserialize them and publish them to the runner callback.
    /// The reader keeps track of how long it has been since the last event has been revieved from the stream. If this is longer
    /// than the configured test file timeout then we kill phantom since it is likely stuck in a infinite loop or error.
    /// We make this timeout the test file timeout plus a small (generous) delay time to account for serialization. 
    /// </summary>
    public class TestCaseStreamReader : ITestCaseStreamReader
    {
        private readonly IJsonSerializer jsonSerializer;
        private readonly IFileProbe fileProbe;
        private readonly Regex prefixRegex = new Regex("^#_#(?<type>[a-z]+)#_#(?<json>.*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Tracks the last time we got an event/update from phantom. 
        private DateTime lastTestEvent;

        public TestCaseStreamReader(IJsonSerializer jsonSerializer, IFileProbe fileProbe)
        {
            this.jsonSerializer = jsonSerializer;
            this.fileProbe = fileProbe;
        }

        public TestCaseSummary Read(ProcessStream processStream, TestOptions testOptions, TestContext testContext, ITestMethodRunnerCallback callback, bool debugEnabled)
        {
            if (processStream == null) throw new ArgumentNullException("processStream");
            if (testOptions == null) throw new ArgumentNullException("testOptions");
            if (testContext == null) throw new ArgumentNullException("testContext");

            lastTestEvent = DateTime.Now;
            var timeout = testOptions.TestFileTimeoutMilliseconds + 500; // Add buffer to timeout to account for serialization
            var readerTask = Task<TestCaseSummary>.Factory.StartNew(() => ReadFromStream(processStream.StreamReader, testContext, callback, debugEnabled));
            while (readerTask.Status == TaskStatus.WaitingToRun
               || (readerTask.Status == TaskStatus.Running && (DateTime.Now - lastTestEvent).TotalMilliseconds < timeout))
            {
                Thread.Sleep(100);
            }

            if (readerTask.IsCompleted)
            {
                return readerTask.Result;
            }
            else
            {
                // We timed out so kill the process
                processStream.KillProcess();
                return null;
            }
        }

        private TestCaseSummary ReadFromStream(StreamReader stream, TestContext testContext, ITestMethodRunnerCallback callback, bool debugEnabled)
        {
            var referencedFile = testContext.ReferencedJavaScriptFiles.SingleOrDefault(x => x.IsFileUnderTest);
            var testIndex = 0;
            var summary = new TestCaseSummary();
            string line;
            while ((line = stream.ReadLine()) != null)
            {
                lastTestEvent = DateTime.Now;
                if (debugEnabled) Console.WriteLine(line);

                var match = prefixRegex.Match(line);
                if (!match.Success) continue;
                var type = match.Groups["type"].Value;
                var json = match.Groups["json"].Value;

                try
                {
                    JsTestCase jsTestCase = null;
                    switch (type)
                    {
                        case "FileStart":
                            callback.FileStarted(testContext.InputTestFile);
                            break;

                        case "FileDone":
                            var jsFileDone = jsonSerializer.Deserialize<JsFileDone>(json);
                            summary.TimeTaken = jsFileDone.TimeTaken;
                            callback.FileFinished(testContext.InputTestFile, summary);
                            break;

                        case "TestStart":
                            jsTestCase = jsonSerializer.Deserialize<JsTestCase>(json);
                            jsTestCase.TestCase.InputTestFile = testContext.InputTestFile;
                            ProcessTestFilePath(jsTestCase.TestCase);
                            callback.TestStarted(jsTestCase.TestCase);
                            break;

                        case "TestDone":
                            jsTestCase = jsonSerializer.Deserialize<JsTestCase>(json);
                            jsTestCase.TestCase.InputTestFile = testContext.InputTestFile;
                            ProcessTestFilePath(jsTestCase.TestCase);
                            AddLineNumber(referencedFile, testIndex, jsTestCase);
                            testIndex++;
                            callback.TestFinished(jsTestCase.TestCase);
                            summary.Tests.Add(jsTestCase.TestCase);
                            break;

                        case "Log":
                            var log = jsonSerializer.Deserialize<JsLog>(json);
                            log.Log.InputTestFile = testContext.InputTestFile;
                            callback.FileLog(log.Log);
                            summary.Logs.Add(log.Log);
                            break;

                        case "Error":
                            var error = jsonSerializer.Deserialize<JsError>(json);
                            error.Error.InputTestFile = testContext.InputTestFile;
                            callback.FileError(error.Error);
                            summary.Errors.Add(error.Error);
                            break;
                    }
                }
                catch (SerializationException)
                {
                    // Ignore malformed json and move on
                }
            }

            return summary;
        }

        /// <summary>
        /// Test the returned test file path to see if it is real and get a normalized version of it.
        /// If the file doesn't exist use the input test file instead.
        /// </summary>
        /// <param name="testCase"></param>
        private void ProcessTestFilePath(TestCase testCase)
        {
            var path = fileProbe.FindFilePath(testCase.TestFile);
            if(path == null)
            {
                testCase.TestFile = testCase.InputTestFile;
            }
            else
            {
                testCase.TestFile = path;
            }
        }

        private static void AddLineNumber(ReferencedFile referencedFile, int testIndex, JsTestCase jsTestCase)
        {
            if (referencedFile != null && referencedFile.FilePositions.Contains(testIndex))
            {
                var position = referencedFile.FilePositions[testIndex];
                jsTestCase.TestCase.Line = position.Line;
                jsTestCase.TestCase.Column = position.Column;
            }
        }
    }
}