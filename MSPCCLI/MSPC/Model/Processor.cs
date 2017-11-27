﻿/** Copyright © 2014-2015 Vahid Jalili
 * 
 * This file is part of MSPC project.
 * MSPC is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 * MSPC is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A 
 * PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with Foobar. If not, see http://www.gnu.org/licenses/.
 **/

using Genometric.IGenomics;
using Genometric.MSPC.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Genometric.MSPC.Core.IntervalTree;
using System.Collections.ObjectModel;
using Genometric.MSPC.Core.XSquaredData;

namespace Genometric.MSPC.Core.Model
{
    internal class Processor<Peak, Metadata>
        where Peak : IInterval<int, Metadata>, IComparable<Peak>, new()
        where Metadata : IChIPSeqPeak, IComparable<Metadata>, new()
    {
        public delegate void ProgressUpdate(ProgressReport value);
        public event ProgressUpdate OnProgressUpdate;

        private double tXsqrd { set; get; }

        private Dictionary<uint, AnalysisResult<Peak, Metadata>> _analysisResults { set; get; }
        public ReadOnlyDictionary<uint, AnalysisResult<Peak, Metadata>> analysisResults
        {
            get { return new ReadOnlyDictionary<uint, AnalysisResult<Peak, Metadata>>(_analysisResults); }
        }

        private Dictionary<uint, Dictionary<string, Tree<Peak, Metadata>>> _trees { set; get; }

        private Dictionary<string, SortedList<Peak, Peak>> _mergedReplicates { set; get; }
        public ReadOnlyDictionary<string, SortedList<Peak, Peak>> mergedReplicates
        {
            get { return new ReadOnlyDictionary<string, SortedList<Peak, Peak>>(_mergedReplicates); }
        }

        private List<double> _cachedChiSqrd { set; get; }

        private List<Peak> sourcePeaks { set; get; }

        private uint sampleHashKey { set; get; }

        private Config _config { set; get; }

        /// <summary>
        /// <list type="bullet">
        /// <item><description>
        ///     uint: sample ID.
        /// </description></item>
        /// <item><description>
        ///     string: chromosome label.
        /// </description></item>
        /// <item><description>
        ///     char: chromosome strand.
        /// </description></item>
        /// </list>
        /// </summary>
        private Dictionary<uint, Dictionary<string, Dictionary<char, List<Peak>>>> _samples { set; get; }

        internal Processor()
        {
            _samples = new Dictionary<uint, Dictionary<string, Dictionary<char, List<Peak>>>>();
        }

        internal void AddSample(uint id, Dictionary<string, Dictionary<char, List<Peak>>> peaks)
        {
            _samples.Add(id, peaks);
        }

        internal ReadOnlyDictionary<uint, AnalysisResult<Peak, Metadata>> Run(Config config)
        {
            int step = 1, stepCount = 6;
            OnProgressUpdate(new ProgressReport(step++, stepCount, "Initializing"));

            _config = config;
            _cachedChiSqrd = new List<double>();
            for (int i = 1; i <= _samples.Count; i++)
                _cachedChiSqrd.Add(Math.Round(ChiSquaredCache.ChiSqrdINVRTP(config.gamma, (byte)(i * 2)), 3));

            _trees = new Dictionary<uint, Dictionary<string, Tree<Peak, Metadata>>>();
            _analysisResults = new Dictionary<uint, AnalysisResult<Peak, Metadata>>();
            foreach (var sample in _samples)
            {
                _trees.Add(sample.Key, new Dictionary<string, Tree<Peak, Metadata>>());
                _analysisResults.Add(sample.Key, new AnalysisResult<Peak, Metadata>());
                foreach (var chr in sample.Value)
                {
                    _trees[sample.Key].Add(chr.Key, new Tree<Peak, Metadata>());
                    _analysisResults[sample.Key].AddChromosome(chr.Key);
                    foreach (var strand in chr.Value)
                        foreach (Peak p in strand.Value)
                            if (p.metadata.value <= _config.tauW)
                                _trees[sample.Key][chr.Key].Add(p);
                            else
                                _analysisResults[sample.Key].R_j__b[chr.Key].Add(p);
                }
            }

            OnProgressUpdate(new ProgressReport(step++, stepCount, "Processing samples"));
            foreach (var sample in _samples)
                foreach (var chr in sample.Value)
                    foreach (var strand in chr.Value)
                        foreach (Peak peak in strand.Value)
                        {
                            tXsqrd = 0;
                            InitialClassification(sample.Key, chr.Key, peak);
                            if (peak.metadata.value <= _config.tauS || peak.metadata.value <= _config.tauW)
                                SecondaryClassification(sample.Key, chr.Key, peak, FindSupportingPeaks(sample.Key, chr.Key, peak));
                        }




            OnProgressUpdate(new ProgressReport(step++, stepCount, "Processing intermediate sets"));
            IntermediateSetsPurification();

            OnProgressUpdate(new ProgressReport(step++, stepCount, "Creating output set"));
            CreateOuputSet();

            OnProgressUpdate(new ProgressReport(step++, stepCount, "Performing Multiple testing correction"));
            CalculateFalseDiscoveryRate();

            OnProgressUpdate(new ProgressReport(step++, stepCount, "Creating consensus peaks set"));
            CreateCombinedOutputSet();
            return analysisResults;
        }

        private void InitialClassification(uint id, string chr, Peak p)
        {
            if (p.metadata.value <= _config.tauS)
                _analysisResults[id].R_j__s[chr].Add(p);            
            else if (p.metadata.value <= _config.tauW)            
                _analysisResults[id].R_j__w[chr].Add(p);
        }

        private List<AnalysisResult<Peak, Metadata>.SupportingPeak> FindSupportingPeaks(uint id, string chr, Peak p)
        {
            var supPeak = new List<AnalysisResult<Peak, Metadata>.SupportingPeak>();
            foreach(var tree in _trees)
            {
                if (tree.Key == id)
                    continue;

                var interPeaks = new List<Peak>();
                if (_trees[tree.Key].ContainsKey(chr))
                    interPeaks = _trees[tree.Key][chr].GetIntervals(p);

                switch (interPeaks.Count)
                {
                    case 0: break;

                    case 1:
                        supPeak.Add(new AnalysisResult<Peak, Metadata>.SupportingPeak()
                        {
                            peak = interPeaks[0],
                            sampleID = tree.Key
                        });
                        break;

                    default:
                        var chosenPeak = interPeaks[0];
                        foreach (var tIp in interPeaks.Skip(1))
                            if ((_config.multipleIntersections == MultipleIntersections.UseLowestPValue && tIp.metadata.value < chosenPeak.metadata.value) ||
                                (_config.multipleIntersections == MultipleIntersections.UseHighestPValue && tIp.metadata.value > chosenPeak.metadata.value))
                                chosenPeak = tIp;

                        supPeak.Add(new AnalysisResult<Peak, Metadata>.SupportingPeak()
                        {
                            peak = chosenPeak,
                            sampleID = tree.Key
                        });
                        break;
                }
            }

            return supPeak;
        }

        private void SecondaryClassification(uint id, string chr, Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks)
        {
            if (supportingPeaks.Count + 1 >= _config.C)
            {
                CalculateXsqrd(p, supportingPeaks);

                if (tXsqrd >= _cachedChiSqrd[supportingPeaks.Count])
                    ConfirmPeak(id, chr, p, supportingPeaks);
                else
                    DiscardPeak(id, chr, p, supportingPeaks, 0);
            }
            else
            {
                DiscardPeak(id, chr, p, supportingPeaks, 1);
            }
        }

        private void ConfirmPeak(uint id, string chr, Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks)
        {
            var anRe = new AnalysisResult<Peak, Metadata>.ProcessedPeak()
            {
                peak = p,
                xSquared = tXsqrd,
                rtp = ChiSquaredCache.ChiSqrdDistRTP(tXsqrd, 2 + (supportingPeaks.Count * 2)),
                supportingPeaks = supportingPeaks
            };

            if (p.metadata.value <= _config.tauS)
            {
                _analysisResults[id].R_j___sc[chr]++;
                anRe.classification = PeakClassificationType.StringentConfirmed;
            }
            else
            {
                _analysisResults[id].R_j___wc[chr]++;
                anRe.classification = PeakClassificationType.WeakConfirmed;
            }

            if (!_analysisResults[id].R_j__c[chr].ContainsKey(p.metadata.hashKey))
                _analysisResults[id].R_j__c[chr].Add(p.metadata.hashKey, anRe);

            ConfirmeSupportingPeaks(chr, p, supportingPeaks);
        }

        private void ConfirmeSupportingPeaks(string chr, Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks)
        {
            foreach (var supPeak in supportingPeaks)
            {
                if (!_analysisResults[supPeak.sampleID].R_j__c[chr].ContainsKey(supPeak.peak.metadata.hashKey))
                {
                    var tSupPeak = new List<AnalysisResult<Peak, Metadata>.SupportingPeak>();
                    var targetSample = _analysisResults[supPeak.sampleID];
                    tSupPeak.Add(new AnalysisResult<Peak, Metadata>.SupportingPeak() { peak = p, sampleID = sampleHashKey });

                    foreach (var sP in supportingPeaks)
                        if (supPeak.CompareTo(sP) != 0)
                            tSupPeak.Add(sP);

                    var anRe = new AnalysisResult<Peak, Metadata>.ProcessedPeak()
                    {
                        peak = supPeak.peak,
                        xSquared = tXsqrd,
                        rtp = ChiSquaredCache.ChiSqrdDistRTP(tXsqrd, 2 + (supportingPeaks.Count * 2)),
                        supportingPeaks = tSupPeak
                    };

                    if (supPeak.peak.metadata.value <= _config.tauS)
                    {
                        targetSample.R_j___sc[chr]++;
                        anRe.classification = PeakClassificationType.StringentConfirmed;
                    }
                    else
                    {
                        targetSample.R_j___wc[chr]++;
                        anRe.classification = PeakClassificationType.WeakConfirmed;
                    }

                    targetSample.R_j__c[chr].Add(supPeak.peak.metadata.hashKey, anRe);
                }
            }
        }

        private void DiscardPeak(uint id, string chr, Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks, byte discardReason)
        {
            var anRe = new AnalysisResult<Peak, Metadata>.ProcessedPeak
            {
                peak = p,
                xSquared = tXsqrd,
                reason = discardReason,
                supportingPeaks = supportingPeaks
            };

            if (p.metadata.value <= _config.tauS)
            {
                // The cause of discarding the region is :
                if (supportingPeaks.Count + 1 >= _config.C)
                    _analysisResults[id].R_j__sdt[chr]++;  // - Test failure
                else _analysisResults[id].R_j__sdc[chr]++; // - insufficient intersecting regions count

                anRe.classification = PeakClassificationType.StringentDiscarded;
            }
            else
            {
                // The cause of discarding the region is :
                if (supportingPeaks.Count + 1 >= _config.C)
                    _analysisResults[id].R_j__wdt[chr]++;  // - Test failure
                else _analysisResults[id].R_j__wdc[chr]++; // - insufficient intersecting regions count

                anRe.classification = PeakClassificationType.WeakDiscarded;
            }

            if (!_analysisResults[id].R_j__d[chr].ContainsKey(p.metadata.hashKey))
                _analysisResults[id].R_j__d[chr].Add(p.metadata.hashKey, anRe);

            if (supportingPeaks.Count + 1 >= _config.C)
                DiscardSupportingPeaks(chr, p, supportingPeaks, discardReason);
        }

        private void DiscardSupportingPeaks(string chr, Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks, byte discardReason)
        {
            foreach (var supPeak in supportingPeaks)
            {
                if (!_analysisResults[supPeak.sampleID].R_j__d[chr].ContainsKey(supPeak.peak.metadata.hashKey))
                {
                    var tSupPeak = new List<AnalysisResult<Peak, Metadata>.SupportingPeak>();
                    var targetSample = _analysisResults[supPeak.sampleID];
                    tSupPeak.Add(new AnalysisResult<Peak, Metadata>.SupportingPeak() { peak = p, sampleID = sampleHashKey });

                    foreach (var sP in supportingPeaks)
                        if (supPeak.CompareTo(sP) != 0)
                            tSupPeak.Add(sP);

                    var anRe = new AnalysisResult<Peak, Metadata>.ProcessedPeak()
                    {
                        peak = supPeak.peak,
                        xSquared = tXsqrd,
                        reason = discardReason,
                        rtp = ChiSquaredCache.ChiSqrdDistRTP(tXsqrd, 2 + (supportingPeaks.Count * 2)),
                        supportingPeaks = tSupPeak
                    };

                    if (supPeak.peak.metadata.value <= _config.tauS)
                    {
                        targetSample.R_j__sdt[chr]++;
                        anRe.classification = PeakClassificationType.StringentDiscarded;
                    }
                    else
                    {
                        targetSample.R_j__wdt[chr]++;
                        anRe.classification = PeakClassificationType.WeakDiscarded;
                    }

                    targetSample.R_j__d[chr].Add(supPeak.peak.metadata.hashKey, anRe);
                }
            }
        }

        private void CalculateXsqrd(Peak p, List<AnalysisResult<Peak, Metadata>.SupportingPeak> supportingPeaks)
        {
            if (p.metadata.value != 0)
                tXsqrd = Math.Log(p.metadata.value, Math.E);
            else
                tXsqrd = Math.Log(Config.default0PValue, Math.E);

            foreach (var supPeak in supportingPeaks)
                if (supPeak.peak.metadata.value != 0)
                    tXsqrd += Math.Log(supPeak.peak.metadata.value, Math.E);
                else
                    tXsqrd += Math.Log(Config.default0PValue, Math.E);

            tXsqrd = tXsqrd * (-2);

            if (tXsqrd >= Math.Abs(Config.defaultMaxLogOfPVvalue))
                tXsqrd = Math.Abs(Config.defaultMaxLogOfPVvalue);
        }

        internal void IntermediateSetsPurification()
        {
            if (_config.replicateType == ReplicateType.Biological)
            {
                // Performe : R_j__d = R_j__d \ { R_j__d intersection R_j__c }

                foreach(var result in _analysisResults)
                {
                    foreach(var chr in result.Value.R_j__c)
                    {
                        foreach (var confirmedPeak in chr.Value)
                        {
                            if (result.Value.R_j__d[chr.Key].ContainsKey(confirmedPeak.Key))
                            {
                                if (confirmedPeak.Value.peak.metadata.value <= _config.tauS)
                                    result.Value.total_scom++;
                                else if (confirmedPeak.Value.peak.metadata.value <= _config.tauW)
                                    result.Value.total_wcom++;

                                result.Value.R_j__d[chr.Key].Remove(confirmedPeak.Key);
                            }
                        }
                    }
                }
            }
            else
            {
                // Performe : R_j__c = R_j__c \ { R_j__c intersection R_j__d }

                foreach(var result in _analysisResults)
                {
                    foreach(var chr in result.Value.R_j__d)
                    {
                        foreach (var discardedPeak in chr.Value)
                        {
                            if (result.Value.R_j__c[chr.Key].ContainsKey(discardedPeak.Key))
                            {
                                if (discardedPeak.Value.peak.metadata.value <= _config.tauS)
                                    result.Value.total_scom++;
                                else if (discardedPeak.Value.peak.metadata.value <= _config.tauW)
                                    result.Value.total_wcom++;

                                result.Value.R_j__c[chr.Key].Remove(discardedPeak.Key);
                            }
                        }
                    }
                }
            }
        }

        internal void CreateOuputSet()
        {
            foreach(var result in _analysisResults)
            {
                foreach(var chr in result.Value.R_j__c)
                {
                    foreach (var confirmedPeak in chr.Value)
                    {
                        var outputPeak = new AnalysisResult<Peak, Metadata>.ProcessedPeak()
                        {
                            peak = confirmedPeak.Value.peak,
                            rtp = confirmedPeak.Value.rtp,
                            xSquared = confirmedPeak.Value.xSquared,
                            classification = PeakClassificationType.TruePositive,
                            supportingPeaks = confirmedPeak.Value.supportingPeaks,
                        };

                        if (confirmedPeak.Value.peak.metadata.value <= _config.tauS)
                        {
                            outputPeak.classification = PeakClassificationType.StringentConfirmed;
                            result.Value.R_j___so[chr.Key]++;
                        }
                        else if (confirmedPeak.Value.peak.metadata.value <= _config.tauW)
                        {
                            outputPeak.classification = PeakClassificationType.WeakConfirmed;
                            result.Value.R_j___wo[chr.Key]++;
                        }

                        result.Value.R_j__o[chr.Key].Add(outputPeak);
                    }
                }
            }
        }

        /// <summary>
        /// Benjamini–Hochberg procedure (step-up procedure)
        /// </summary>
        internal void CalculateFalseDiscoveryRate()
        {
            foreach(var result in _analysisResults)
            {
                foreach(var chr in result.Value.R_j__o)
                {
                    result.Value.R_j_TP[chr.Key] = (uint)chr.Value.Count;
                    result.Value.R_j_FP[chr.Key] = 0;

                    var outputSet = result.Value.R_j__o[chr.Key];

                    int m = outputSet.Count();

                    // Sorts output set based on the values of peaks. 
                    outputSet.Sort(new Comparers.CompareProcessedPeakByValue<Peak, Metadata>());

                    for (int k = 1; k <= m; k++)
                    {
                        if (outputSet[k - 1].peak.metadata.value > ((double)k / (double)m) * _config.alpha)
                        {
                            k--;

                            for (int l = 1; l < k; l++)
                            {
                                // This should update the [analysisResults[sample.Key].R_j__o[chr.Key]] ; is it updating ?
                                outputSet[l].adjPValue = (((double)k * outputSet[l].peak.metadata.value) / (double)m) * _config.alpha;
                                outputSet[l].statisticalClassification = PeakClassificationType.TruePositive;
                            }
                            for (int l = k; l < m; l++)
                            {
                                outputSet[l].adjPValue = (((double)k * outputSet[l].peak.metadata.value) / (double)m) * _config.alpha;
                                outputSet[l].statisticalClassification = PeakClassificationType.FalsePositive;
                            }

                            //analysisResults[sample.Key].R_j_TP[chr.Key] = (uint)k;
                            result.Value.R_j_TP[chr.Key] = (uint)k;
                            //analysisResults[sample.Key].R_j_FP[chr.Key] = (uint)(m - k);
                            result.Value.R_j_FP[chr.Key] = (uint)(m - k);

                            break;
                        }
                    }

                    // Sorts output set using default comparer. 
                    // The default sorter gives higher priority to two ends than values. 
                    outputSet.Sort();
                }
            }
        }

        internal void CreateCombinedOutputSet()
        {
            _mergedReplicates = new Dictionary<string, SortedList<Peak, Peak>>();
            foreach (var result in _analysisResults)
            {
                foreach (var chr in result.Value.R_j__o)
                {
                    if (!_mergedReplicates.ContainsKey(chr.Key))
                        _mergedReplicates.Add(chr.Key, new SortedList<Peak, Peak>());

                    foreach (var outputER in chr.Value)
                    {
                        var peak = outputER.peak;
                        var interval = new Peak();
                        interval.left = peak.left;
                        interval.right = peak.right;

                        Peak mergedPeak;
                        Peak mergingPeak = new Peak();
                        mergingPeak.left = peak.left;
                        mergingPeak.right = peak.right;
                        mergingPeak.metadata.value =
                            (-2) * Math.Log((peak.metadata.value == 0 ? Config.default0PValue : peak.metadata.value), Math.E);

                        while (_mergedReplicates[chr.Key].TryGetValue(interval, out mergedPeak))
                        {
                            _mergedReplicates[chr.Key].Remove(interval);
                            interval.left = Math.Min(interval.left, mergedPeak.left);
                            interval.right = Math.Max(interval.right, mergedPeak.right);
                            mergingPeak.left = interval.left;
                            mergingPeak.right = interval.right;
                            mergingPeak.metadata.value += mergedPeak.metadata.value;
                        }

                        if (mergingPeak.metadata.value >= Math.Abs(Config.defaultMaxLogOfPVvalue))
                            mergingPeak.metadata.value = Math.Abs(Config.defaultMaxLogOfPVvalue);
                        
                        _mergedReplicates[chr.Key].Add(interval, mergingPeak);
                    }
                }
            }

            int c = 0;
            foreach (var chr in _mergedReplicates)
                foreach (var peak in chr.Value)
                    peak.Value.metadata.name = "MSPC_peak_" + (c++);
        }
    }
}
