﻿// Licensed to the Genometric organization (https://github.com/Genometric) under one or more agreements.
// The Genometric organization licenses this file to you under the GNU General Public License v3.0 (GPLv3).
// See the LICENSE file in the project root for more information.

using Genometric.GeUtilities.Intervals.Model;
using Genometric.GeUtilities.Intervals.Parsers.Model;
using Genometric.MSPC.Core.Model;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Genometric.MSPC.CLI.Logging
{
    public static class Logger
    {
        private static readonly int _indexColumnWidth = 10;
        private static readonly int _sectionHeaderLenght = 30;
        private static readonly int _fileNameMaxLenght = 20;
        private static readonly string _cannotContinue = "\r\nMSPC cannot continue.";
        private static bool _lastStatusUpdatedItsPrevious;
        private static Table _parserLogTable;

        private static ILog log;

        public static void Setup(string logFilePath)
        {
            LogManager.CreateRepository("mspc");
            Hierarchy hierarchy = (Hierarchy)LogManager.GetRepository("mspc");

            PatternLayout patternLayout = new PatternLayout();
            patternLayout.ConversionPattern = "%date\t[%thread]\t%-5level\t%message%newline";
            patternLayout.ActivateOptions();

            RollingFileAppender roller = new RollingFileAppender
            {
                AppendToFile = false,
                File = logFilePath,
                Layout = patternLayout,
                MaxSizeRollBackups = 5,
                MaximumFileSize = "1GB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true
            };
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            MemoryAppender memory = new MemoryAppender();
            memory.ActivateOptions();
            hierarchy.Root.AddAppender(memory);

            hierarchy.Root.Level = Level.Info;
            hierarchy.Configured = true;
            log = LogManager.GetLogger("mspc", "log");

            log.Info("NOTE THAT THE LOG PATTERN IS: <Date> <#Thread> <Level> <Message>");
        }

        public static void LogStartOfASection(string header)
        {
            string msg = ".::." + header.PadLeft(((_sectionHeaderLenght - header.Length) / 2) + header.Length, '.').PadRight(_sectionHeaderLenght, '.') + ".::.";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(Environment.NewLine + msg + Environment.NewLine);
            Console.ResetColor();
            log.Info(msg);
        }

        public static void LogException(Exception e)
        {
            LogException(e.Message + _cannotContinue);
        }

        public static void LogException(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
            log.Error(message);
        }

        public static void LogMSPCStatus(object sender, ValueEventArgs e)
        {
            var msg = new StringBuilder();
            var report = e.Value;
            if (report.UpdatesPrevious)
                msg.Append("\r");

            if (report.SubStep)
                msg.Append(string.Format(
                    "  └── {0}/{1}\t({2})\t{3}",
                    report.Step.ToString("N0"),
                    report.StepCount.ToString("N0"),
                    (report.Step / (double)report.StepCount).ToString("P"),
                    report.Message ?? ""));
            else
                msg.Append(string.Format(
                    "[{0}/{1}] {2}",
                    report.Step,
                    report.StepCount,
                    report.Message));

            log.Info(msg.ToString());
            if (report.UpdatesPrevious)
            {
                Console.Write(msg.ToString());
                _lastStatusUpdatedItsPrevious = true;
            }
            else
            {
                if (_lastStatusUpdatedItsPrevious)
                    msg.Insert(0, Environment.NewLine);
                Console.WriteLine(msg.ToString());
                _lastStatusUpdatedItsPrevious = false;
            }
        }

        public static void Log(string message)
        {
            Console.WriteLine(message);
            log.Info(message);
        }

        public static void LogFinish()
        {
            string msg = Environment.NewLine + "All processes successfully finished" + Environment.NewLine;
            Console.WriteLine(msg);
            log.Info(msg);
        }

        public static void InitializeLoggingParser()
        {
            var columnsWidth = new int[] { _indexColumnWidth, _fileNameMaxLenght, 11, 11, 12, 11 };
            _parserLogTable = new Table(columnsWidth);
            _parserLogTable.AddHeader(new string[]
            {
                "#", "Filename", "Read peaks#", "Min p-value", "Mean p-value", "Max p-value"
            });
        }

        public static void LogParser(
            int fileNumber,
            int filesToParse,
            string filename,
            int peaksCount,
            double minPValue,
            double meanPValue,
            double maxPValue)
        {
            _parserLogTable.AddRow(new string[]
            {
                string.Format("{0}/{1}", fileNumber.ToString("N0"), filesToParse.ToString("N0")),
                filename,
                peaksCount.ToString("N0"),
                string.Format("{0:E3}", minPValue),
                string.Format("{0:E3}", meanPValue),
                string.Format("{0:E3}", maxPValue)
            });
        }

        public static void LogSummary(
            List<Bed<Peak>> samples,
            Dictionary<uint, string> samplesDict,
            ReadOnlyDictionary<uint, Result<Peak>> results,
            ReadOnlyDictionary<string, List<ProcessedPeak<Peak>>> consensusPeaks,
            List<Attributes> exportedAttributes = null)
        {
            // Create table header
            int i;
            int columnsCount = exportedAttributes.Count + 3;
            int[] columnsWidth = new int[columnsCount];
            var headerColumns = new string[columnsCount];
            headerColumns[0] = "#";
            headerColumns[1] = "Filename";
            headerColumns[2] = "Read peaks#";
            columnsWidth[0] = _indexColumnWidth;
            columnsWidth[1] = _fileNameMaxLenght;
            columnsWidth[2] = headerColumns[2].Length;
            for (i = 3; i < columnsCount; i++)
            {
                headerColumns[i] = exportedAttributes[i - 3].ToString();
                columnsWidth[i] = headerColumns[i].Length > 8 ? headerColumns[i].Length : 8;
            }
            var table = new Table(columnsWidth);
            table.AddHeader(headerColumns);

            // Per sample stats
            int j = 1;
            foreach (var res in results)
            {
                double totalPeaks = samples.Find(x => x.FileHashKey == res.Key).IntervalsCount;
                var sampleSummary = new string[columnsCount];
                sampleSummary[0] = string.Format("{0}/{1}", (j++).ToString("N0"), results.Count.ToString("N0"));
                sampleSummary[1] = samplesDict[res.Key];
                sampleSummary[2] = totalPeaks.ToString("N0");
                i = 3;
                foreach (var att in exportedAttributes)
                {
                    int value = 0;
                    foreach (var chr in res.Value.Chromosomes)
                        value += chr.Value.Count(att);
                    sampleSummary[i++] = (value / totalPeaks).ToString("P");
                }
                table.AddRow(sampleSummary);
            }
        }
    }
}
