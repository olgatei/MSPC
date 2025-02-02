﻿// Licensed to the Genometric organization (https://github.com/Genometric) under one or more agreements.
// The Genometric organization licenses this file to you under the GNU General Public License v3.0 (GPLv3).
// See the LICENSE file in the project root for more information.

using Genometric.GeUtilities.Intervals.Parsers;
using Genometric.MSPC.CLI.Tests.MockTypes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Genometric.MSPC.CLI.Tests
{
    public class Main
    {
        [Fact]
        public void ErrorIfLessThanTwoSamplesAreGiven()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(false, "-i rep1.bed -r bio -w 1E-2 -s 1E-8");

            // Assert
            Assert.Contains("At least two samples are required; 1 is given.", msg);
        }

        [Fact]
        public void ErrorIfARequiredArgumentIsMissing()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(false, "-i rep1.bed -i rep2.bed -w 1E-2 -s 1E-8");

            // Assert
            Assert.Contains("The following required arguments are missing: r|replicate;", msg);
        }

        [Fact]
        public void ErrorIfASpecifiedFileIsMissing()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(false, "-i rep1.bed -i rep2.bed -r bio -w 1E-2 -s 1E-8");

            // Assert
            Assert.Contains("The following files are missing: rep1.bed; rep2.bed", msg);
        }

        [Fact]
        public void AssertInformingPeaksCount()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run();

            // Assert
            Assert.Contains("  2\t", msg);
            Assert.Contains("  3\t", msg);
        }

        [Fact]
        public void AssertInformingMinPValue()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run();

            // Assert
            Assert.Contains("1.000E-005", msg);
            Assert.Contains("1.000E-007", msg);
        }

        [Fact]
        public void AssertInformingMaxPValue()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run();

            // Assert
            Assert.Contains("1.000E-003", msg);
            Assert.Contains("1.000E-002", msg);
        }

        [Fact]
        public void SuccessfulAnalysis()
        {
            // Arrange
            string msg;

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run();

            // Assert
            Assert.Contains("All processes successfully finished", msg);
        }

        [Theory]
        [InlineData("-?")]
        [InlineData("-h")]
        [InlineData("--help")]
        public void ShowsHelpText(string template)
        {
            // Arrange
            string msg;
            string expected =
                "\r\n\r\nUsage: MSPC CLI [options]\r\n\r\nOptions:" +
                "\r\n  -? | -h | --help                      Show help information" +
                "\r\n  -v | --version                        Show version information" +
                "\r\n  -i | --input <value>                  Input samples to be processed in Browser Extensible Data (BED) Format." +
                "\r\n  -r | --replicate <value>              Sets the replicate type of samples. Possible values are: { Bio, Biological, Tec, Technical }" +
                "\r\n  -w | --tauW <value>                   Sets weak threshold. All peaks with p-values higher than this value are considered as weak peaks." +
                "\r\n  -s | --tauS <value>                   Sets stringency threshold. All peaks with p-values lower than this value are considered as stringent peaks." +
                "\r\n  -g | --gamma <value>                  Sets combined stringency threshold. The peaks with their combined p-values satisfying this threshold will be confirmed." +
                "\r\n  -a | --alpha <value>                  Sets false discovery rate of Benjamini–Hochberg step-up procedure." +
                "\r\n  -c <value>                            Sets minimum number of overlapping peaks before combining p-values." +
                "\r\n  -m | --multipleIntersections <value>  When multiple peaks from a sample overlap with a given peak, this argument defines which of the peaks to be considered: the one with lowest p-value, or the one with highest p-value? Possible values are: { Lowest, Highest }" +
                "\r\n  -d | --degreeOfParallelism <value>    Set the degree of parallelism." +
                "\r\n  -p | --parser <value>                 Sets the path to the parser configuration file in JSON." +
                "\r\n  -o | --output <value>                 Sets a path where analysis results should be persisted." +
                "\r\n" +
                "\n\rDocumentation:\thttps://genometric.github.io/MSPC/" +
                "\n\rSource Code:\thttps://github.com/Genometric/MSPC" +
                "\n\rPublications:\thttps://genometric.github.io/MSPC/publications" +
                "\n\r\r\n";

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(template: template);

            // Assert
            Assert.Contains(expected, msg);
        }

        [Theory]
        [InlineData("-v")]
        [InlineData("--version")]
        public void ShowVersion(string template)
        {
            // Arrange
            string msg;
            string expected = "\r\nVersion ";

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(template: template);

            // Assert
            Assert.Contains(expected, msg);
        }

        [Fact]
        public void GenerateOutputPathIfNotGiven()
        {
            // Arrange
            var o = new Orchestrator();

            // Act
            o.Orchestrate("-i rep1.bed -i rep2.bed -r bio -w 1E-2 -s 1E-8".Split(' '));

            // Assert
            Assert.True(!string.IsNullOrEmpty(o.OutputPath) && !string.IsNullOrWhiteSpace(o.OutputPath));
        }

        [Fact]
        public void AppendNumberToGivenPathIfAlreadyExists()
        {
            // Arrange
            var o = new Orchestrator();
            string baseName = @"TT" + new Random().Next().ToString();
            var dirs = new List<string>
            {
                baseName, baseName + "0", baseName + "1"
            };

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
                File.Create(dir + Path.DirectorySeparatorChar + "test").Dispose();
            }

            // Act
            o.Orchestrate(string.Format("-i rep1.bed -i rep2.bed -r bio -w 1E-2 -s 1E-8 -o {0}", dirs[0]).Split(' '));

            // Assert
            Assert.Equal(o.OutputPath, dirs[0] + "2");

            // Clean up
            o.Dispose();
            foreach (var dir in dirs)
                Directory.Delete(dir, true);
            Directory.Delete(dirs[0] + "2", true);
        }

        [Fact]
        public void RaiseExceptionWritingToIllegalPath()
        {
            // Arrange
            string msg;
            var illegalPath = "C:\\*<>*\\//";

            // Act
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(sessionPath: illegalPath);

            // Assert
            Assert.Contains("Illegal characters in path.", msg);
        }

        [Fact]
        public void ReuseExistingLogger()
        {
            // Arrange
            List<string> messages;

            // Act
            using (var tmpMspc = new TmpMspc())
                messages = tmpMspc.FailRun();

            // Assert
            Assert.Contains(messages, x => x.Contains("The following files are missing: rep1; rep2"));
            Assert.Contains(messages, x => x.Contains("The following required arguments are missing: i|input"));
        }

        [Fact]
        public void DontReportSuccessfullyFinishedIfExitedAfterAnError()
        {
            // Arrange
            List<string> messages;

            // Act
            using (var tmpMspc = new TmpMspc())
                messages = tmpMspc.FailRun();

            // Assert
            Assert.DoesNotContain(messages, x => x.Contains("All processes successfully finished"));
        }

        [Fact]
        public void WriteOutputPathExceptionToLoggerIfAvailable()
        {
            // Arrange
            List<string> messages;

            // Act
            using (var tmpMspc = new TmpMspc())
                messages = tmpMspc.FailRun(template2: "-i rep1 -i rep2 -o C:\\*<>*\\// -r bio -s 1e-8 -w 1e-4");

            // Assert
            Assert.Contains(messages, x => x.Contains("The following files are missing: rep1; rep2"));
            Assert.Contains(messages, x => x.Contains("Illegal characters in path."));
        }

        [Fact]
        public void CaptureExporterExceptions()
        {
            // Arrange
            string message;

            // Act
            using (var tmpMspc = new TmpMspc())
                message = tmpMspc.Run(new MExporter());

            // Assert
            Assert.Contains("The method or operation is not implemented.", message);
            Assert.DoesNotContain("All processes successfully finished", message);
        }

        [Fact]
        public void CaptureExceptionsRaisedCreatingLogger()
        {
            // Arrange
            string rep1Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";
            string rep2Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";

            var o = new Orchestrator
            {
                loggerTimeStampFormat = "yyyyMMdd_HHmmssffffffffffff"
            };

            // Act
            string output;
            using (StringWriter sw = new StringWriter())
            {
                Console.SetOut(sw);
                o.Orchestrate(string.Format("-i {0} -i {1} -r bio -w 1e-2 -s 1e-4", rep1Path, rep2Path).Split(' '));
                output = sw.ToString();
            }
            var standardOutput = new StreamWriter(Console.OpenStandardOutput())
            {
                AutoFlush = true
            };
            Console.SetOut(standardOutput);

            // Assert
            Assert.Contains("Input string was not in a correct format.", output);
        }

        [Fact]
        public void ReadDataAccordingToParserConfig()
        {
            // Arrange
            ParserConfig cols = new ParserConfig()
            {
                Chr = 0,
                Left = 3,
                Right = 4,
                Name = 1,
                Strand = 2,
                Summit = 6,
                Value = 5,
                DefaultValue = 1.23E-45,
                PValueFormat = PValueFormats.minus1_Log10_pValue,
                DropPeakIfInvalidValue = false,
            };
            var path = Environment.CurrentDirectory + Path.DirectorySeparatorChar + "MSPCTests_" + new Random().NextDouble().ToString();
            using (StreamWriter w = new StreamWriter(path))
                w.WriteLine(JsonConvert.SerializeObject(cols));

            string rep1Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";
            string rep2Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";

            FileStream stream = File.Create(rep1Path);
            using (StreamWriter writter = new StreamWriter(stream))
                writter.WriteLine("chr1\tMSPC_PEAK\t.\t10\t20\t16\t15");

            stream = File.Create(rep2Path);
            using (StreamWriter writter = new StreamWriter(stream))
                writter.WriteLine("chr1\tMSPC_PEAK\t.\t15\t25\tEEE\t20");

            // Act
            string msg;
            using (var tmpMspc = new TmpMspc())
                msg = tmpMspc.Run(createSample: false, template: string.Format("-i {0} -i {1} -p {2} -r bio -w 1e-2 -s 1e-4", rep1Path, rep2Path, path));

            // Assert
            Assert.Contains("1.000E-016", msg);
            Assert.Contains("1.230E-045", msg);
        }

        [Fact]
        public void CaptureExceptionReadingFile()
        {
            // Arrange
            string rep1Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";
            string rep2Path = Path.GetTempPath() + Guid.NewGuid().ToString() + ".bed";

            new StreamWriter(rep1Path).Close();
            new StreamWriter(rep2Path).Close();

            // Lock the file so the parser cannot access it.
            var fs = new FileStream(path: rep1Path, mode: FileMode.OpenOrCreate, access: FileAccess.Write, share: FileShare.None);

            // Act
            string logFile;
            string path;
            using (var o = new Orchestrator())
            {
                o.Orchestrate(string.Format("-i {0} -i {1} -r bio -w 1e-2 -s 1e-4", rep1Path, rep2Path).Split(' '));
                logFile = o.LogFile;
                path = o.OutputPath;
            }

            string line;
            var messages = new List<string>();
            using (var reader = new StreamReader(logFile))
                while ((line = reader.ReadLine()) != null)
                    messages.Add(line);
            fs.Close();

            // Assert
            Assert.Contains(messages, x => 
            x.Contains("The process cannot access the file") && 
            x.Contains("because it is being used by another process."));

            // Clean up
            File.Delete(rep1Path);
            File.Delete(rep2Path);
            Directory.Delete(path, true);
        }
    }
}
