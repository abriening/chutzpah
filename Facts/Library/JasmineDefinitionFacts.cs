﻿namespace Chutzpah.Facts.Library
{
    using System.Collections.Generic;
    using Chutzpah.Facts.Properties;
    using Chutzpah.FileProcessors;
    using Chutzpah.FrameworkDefinitions;
    using Chutzpah.Models;
    using Moq;
    using Xunit;
    using Xunit.Extensions;

    public class JasmineDefinitionFacts
    {
        private class JasmineDefinitionCreator : Testable<JasmineDefinition>
        {
            public JasmineDefinitionCreator()
            {
            }
        }

        public class FileUsesFramework
        {
            public static IEnumerable<object[]> TestSuites
            {
                get
                {
                    return new object[][]
                    {
                        new object[] { Resources.JSSpecSuite },
                        new object[] { Resources.JsTestDriverSuite },
                        new object[] { Resources.QUnitSuite },
                        new object[] { Resources.YUITestSuite }
                    };
                }
            }

            [Fact]
            public void ReturnsTrue_WithJasmineSuiteAndDefinitiveDetection()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.True(creator.ClassUnderTest.FileUsesFramework(Resources.JasmineSuite, false, PathType.JavaScript));
            }

            [Fact]
            public void ReturnsTrue_WithJasmineSuiteAndBestGuessDetection()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.True(creator.ClassUnderTest.FileUsesFramework(Resources.JasmineSuite, true, PathType.JavaScript));
            }

            [Fact]
            public void ReturnsTrue_WithCoffeeScriptJasmineSuiteAndDefinitiveDetection()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.True(creator.ClassUnderTest.FileUsesFramework(Resources.JasmineSuiteCoffee, false, PathType.CoffeeScript));
            }

            [Fact]
            public void ReturnsTrue_WithCoffeeScriptJasmineSuiteAndBestGuessDetection()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.True(creator.ClassUnderTest.FileUsesFramework(Resources.JasmineSuiteCoffee, true, PathType.CoffeeScript));
            }

            [Theory]
            [PropertyData("TestSuites")]
            public void ReturnsFalse_WithForeignSuiteAndDefinitiveDetection(string suite)
            {
                var creator = new JasmineDefinitionCreator();

                Assert.False(creator.ClassUnderTest.FileUsesFramework(suite, false, PathType.JavaScript));
            }

            [Theory]
            [PropertyData("TestSuites")]
            public void ReturnsFalse_WithForeignSuiteAndBestGuessDetection(string suite)
            {
                var creator = new JasmineDefinitionCreator();

                Assert.False(creator.ClassUnderTest.FileUsesFramework(suite, true, PathType.JavaScript));
            }
        }

        public class ReferenceIsDependency
        {
            [Fact]
            public void ReturnsTrue_GivenJasmineFile()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.True(creator.ClassUnderTest.ReferenceIsDependency("jasmine.js"));
                Assert.True(creator.ClassUnderTest.ReferenceIsDependency("jasmine-html.js"));
            }

            [Fact]
            public void ReturnsFalse_GivenQUnitFile()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.False(creator.ClassUnderTest.ReferenceIsDependency("qunit.js"));
            }

            [Fact]
            public void ReturnsFalse_GivenEmptyOrNullString()
            {
                var creator = new JasmineDefinitionCreator();

                Assert.False(creator.ClassUnderTest.ReferenceIsDependency(string.Empty));
                Assert.False(creator.ClassUnderTest.ReferenceIsDependency(null));
            }
        }

        public class Process
        {
            [Fact]
            public void CallsDependency_GivenOneProcessor()
            {
                var creator = new JasmineDefinitionCreator();
                var processor = creator.Mock<IJasmineReferencedFileProcessor>();
                processor.Setup(x => x.Process(It.IsAny<ReferencedFile>()));
                creator.ClassUnderTest.Process(new ReferencedFile());

                processor.Verify(x => x.Process(It.IsAny<ReferencedFile>()));
            }

            [Fact]
            public void CallsAllDependencies_GivenMultipleProcessors()
            {
                var creator = new JasmineDefinitionCreator();
                var processor1 = new Mock<IJasmineReferencedFileProcessor>();
                var processor2 = new Mock<IJasmineReferencedFileProcessor>();
                processor1.Setup(x => x.Process(It.IsAny<ReferencedFile>()));
                processor2.Setup(x => x.Process(It.IsAny<ReferencedFile>()));
                creator.InjectArray<IJasmineReferencedFileProcessor>(new[] { processor1.Object, processor2.Object });

                creator.ClassUnderTest.Process(new ReferencedFile());

                processor1.Verify(x => x.Process(It.IsAny<ReferencedFile>()));
                processor2.Verify(x => x.Process(It.IsAny<ReferencedFile>()));
            }
        }

    }
}
