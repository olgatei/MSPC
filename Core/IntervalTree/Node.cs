﻿// Licensed to the Genometric organization (https://github.com/Genometric) under one or more agreements.
// The Genometric organization licenses this file to you under the GNU General Public License v3.0 (GPLv3).
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Genometric.GeUtilities.IGenomics;

namespace Genometric.MSPC.IntervalTree
{
    internal class Node<I>
        where I : IChIPSeqPeak, new()
    {
        private SortedDictionary<I, List<I>> _intervals;
        private int _center;
        private Node<I> _leftNode;
        private Node<I> _rightNode;

        public Node()
        {
            _intervals = new SortedDictionary<I, List<I>>();
            _center = 0;
            _leftNode = null;
            _rightNode = null;
        }

        public Node(List<I> intervalsList)
        {
            _intervals = new SortedDictionary<I, List<I>>();
            var endpoints = new SortedSet<int>();
            foreach (var interval in intervalsList)
            {
                endpoints.Add(interval.Left);
                endpoints.Add(interval.Right);
            }

            int? median = GetMedian(endpoints);
            _center = median.GetValueOrDefault();

            var left = new List<I>();
            var right = new List<I>();

            foreach (I interval in intervalsList)
            {
                if (interval.Right.CompareTo(_center) < 0)
                    left.Add(interval);
                else if (interval.Left.CompareTo(_center) > 0)
                    right.Add(interval);
                else
                {
                    List<I> posting;
                    if (!_intervals.TryGetValue(interval, out posting))
                    {
                        posting = new List<I>();
                        _intervals.Add(interval, posting);
                    }
                    posting.Add(interval);
                }
            }

            if (left.Count > 0)
                _leftNode = new Node<I>(left);
            if (right.Count > 0)
                _rightNode = new Node<I>(right);
        }

        private int? GetMedian(SortedSet<int> set)
        {
            int i = 0;
            int middle = set.Count / 2;
            foreach (var point in set)
            {
                if (i == middle)
                    return point;
                i++;
            }
            return null;
        }

        private List<I> GetIntervalsOfKeys(List<I> intervalKeys)
        {
            var allIntervals =
              from k in intervalKeys
              select _intervals[k];

            return allIntervals.SelectMany(x => x).ToList();
        }

        public IEnumerable<IList<I>> Intersections
        {
            get
            {
                if (_intervals.Count == 0) yield break;
                else if (_intervals.Count == 1)
                {
                    if (_intervals.First().Value.Count > 1)
                    {
                        yield return _intervals.First().Value;
                    }
                }
                else
                {
                    var keys = _intervals.Keys.ToArray();

                    int lastIntervalIndex = 0;
                    var intersectionsKeys = new List<I>();
                    for (int index = 1; index < _intervals.Count; index++)
                    {
                        var intervalKey = keys[index];
                        if (IntervalOperations<I>.Intersects(intervalKey, keys[lastIntervalIndex]))
                        {
                            if (intersectionsKeys.Count == 0)
                            {
                                intersectionsKeys.Add(keys[lastIntervalIndex]);
                            }
                            intersectionsKeys.Add(intervalKey);
                        }
                        else
                        {
                            if (intersectionsKeys.Count > 0)
                            {
                                yield return GetIntervalsOfKeys(intersectionsKeys);
                                intersectionsKeys = new List<I>();
                                index--;
                            }
                            else
                            {
                                if (_intervals[intervalKey].Count > 1)
                                {
                                    yield return _intervals[intervalKey];
                                }
                            }

                            lastIntervalIndex = index;
                        }
                    }

                    if (intersectionsKeys.Count > 0) yield return GetIntervalsOfKeys(intersectionsKeys);
                }
            }
        }

        public List<I> Query(I target)
        {
            List<I> result = new List<I>();

            foreach (var entry in _intervals)
            {
                if (IntervalOperations<I>.Intersects(entry.Key, target))
                    foreach (I interval in entry.Value)
                        result.Add(interval);
                else if (entry.Key.Left.CompareTo(target.Right) > 0)
                    break;
            }

            if (target.Left.CompareTo(_center) < 0 && _leftNode != null)
                result.AddRange(_leftNode.Query(target));
            if (target.Right.CompareTo(_center) > 0 && _rightNode != null)
                result.AddRange(_rightNode.Query(target));
            return result;
        }


        public int Center
        {
            get { return _center; }
            set { _center = value; }
        }

        public Node<I> Left
        {
            get { return _leftNode; }
            set { _leftNode = value; }
        }

        public Node<I> Right
        {
            get { return _rightNode; }
            set { _rightNode = value; }
        }
    }
}
