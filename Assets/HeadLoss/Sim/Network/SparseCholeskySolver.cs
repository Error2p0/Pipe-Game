using System;
using System.Collections.Generic;

namespace HeadLoss.Sim.Network
{
    /// <summary>
    /// Sparse LDLᵀ with a minimum-degree ordering (<c>05-simulation.md</c> §1.3, "sparse Cholesky with AMD
    /// ordering"). The ordering and the symbolic structure are computed once per matrix structure and reused
    /// for every Newton iteration; the numeric factorisation is left-looking. Building networks are close to
    /// trees, so fill-in stays tiny and a solve costs O(nnz). Deterministic: ties break by index.
    /// </summary>
    public sealed class SparseCholeskySolver : ISparseSolver
    {
        SparseMatrix? _structureOf;
        int _n;
        int[] _perm = Array.Empty<int>();     // new index → original index
        int[] _inv = Array.Empty<int>();      // original index → new index
        int[] _colStart = Array.Empty<int>(); // column j of L: rows _rows[_colStart[j] .. _colStart[j+1])
        int[] _rows = Array.Empty<int>();
        double[] _l = Array.Empty<double>();
        double[] _d = Array.Empty<double>();
        // For each row j: the columns k < j with L[j,k] ≠ 0 and the index of that entry.
        int[] _rowStart = Array.Empty<int>();
        int[] _rowCols = Array.Empty<int>();
        int[] _rowIdx = Array.Empty<int>();
        // Permuted A, lower part by column: entries (row ≥ col) of each column in the new numbering.
        int[] _aColStart = Array.Empty<int>();
        int[] _aRows = Array.Empty<int>();
        int[] _aSrc = Array.Empty<int>();
        double[] _work = Array.Empty<double>();
        double[] _y = Array.Empty<double>();

        public int Solve(SparseMatrix a, double[] b, double[] x)
        {
            if (!ReferenceEquals(a, _structureOf) || a.N != _n) Analyse(a);
            Factor(a);
            int n = _n;
            for (int i = 0; i < n; i++) _y[i] = b[_perm[i]];
            for (int j = 0; j < n; j++)
            {
                double yj = _y[j];
                if (yj == 0) continue;
                for (int p = _colStart[j]; p < _colStart[j + 1]; p++) _y[_rows[p]] -= _l[p] * yj;
            }
            for (int j = 0; j < n; j++) _y[j] /= _d[j];
            for (int j = n - 1; j >= 0; j--)
            {
                double s = _y[j];
                for (int p = _colStart[j]; p < _colStart[j + 1]; p++) s -= _l[p] * _y[_rows[p]];
                _y[j] = s;
            }
            for (int i = 0; i < n; i++) x[_perm[i]] = _y[i];
            return 1;
        }

        /// <summary>
        /// Minimum-degree ordering on the elimination graph; the neighbours at elimination are the columns of L.
        /// Leaves are peeled first (no fill, linear time); only the loop core uses the general minimum-degree step.
        /// </summary>
        void Analyse(SparseMatrix a)
        {
            _structureOf = a;
            int n = _n = a.N;
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                adj[i] = new List<int>(a.RowStart[i + 1] - a.RowStart[i]);
                for (int k = a.RowStart[i]; k < a.RowStart[i + 1]; k++)
                    if (a.Columns[k] != i) adj[i].Add(a.Columns[k]);
            }
            _perm = new int[n];
            _inv = new int[n];
            var eliminated = new bool[n];
            var columns = new List<int>[n];
            int step = 0;

            // 1. Leaf peeling (degree ≤ 1), in index order then discovery order.
            var queue = new Queue<int>();
            var queued = new bool[n];
            for (int i = 0; i < n; i++)
                if (adj[i].Count <= 1) { queue.Enqueue(i); queued[i] = true; }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                if (adj[v].Count > 1) { queued[v] = false; continue; }
                eliminated[v] = true;
                _perm[step] = v;
                _inv[v] = step++;
                columns[v] = new List<int>(adj[v]);
                foreach (int u in adj[v])
                {
                    adj[u].Remove(v);
                    if (adj[u].Count <= 1 && !queued[u]) { queue.Enqueue(u); queued[u] = true; }
                }
                adj[v].Clear();
            }

            // 2. Minimum degree on the remaining core (loops), with fill.
            var heap = new SortedSet<(int degree, int node)>();
            for (int i = 0; i < n; i++) if (!eliminated[i]) heap.Add((adj[i].Count, i));
            var neighbours = new List<int>();
            while (heap.Count > 0)
            {
                (int _, int v) = heap.Min;
                heap.Remove(heap.Min);
                eliminated[v] = true;
                _perm[step] = v;
                _inv[v] = step++;
                neighbours.Clear();
                neighbours.AddRange(adj[v]);
                neighbours.Sort();
                columns[v] = new List<int>(neighbours);
                foreach (int u in neighbours)
                {
                    heap.Remove((adj[u].Count, u));
                    adj[u].Remove(v);
                }
                for (int i = 0; i < neighbours.Count; i++)
                    for (int k = i + 1; k < neighbours.Count; k++)
                    {
                        int x = neighbours[i], y = neighbours[k];
                        if (adj[x].Contains(y)) continue;
                        adj[x].Add(y);
                        adj[y].Add(x);
                    }
                foreach (int u in neighbours) heap.Add((adj[u].Count, u));
                adj[v].Clear();
            }

            // Column structure of L in the new numbering (rows > column, sorted).
            _colStart = new int[n + 1];
            for (int j = 0; j < n; j++) _colStart[j + 1] = _colStart[j] + columns[_perm[j]].Count;
            _rows = new int[_colStart[n]];
            _l = new double[_colStart[n]];
            var tmp = new List<int>();
            for (int j = 0; j < n; j++)
            {
                tmp.Clear();
                foreach (int u in columns[_perm[j]]) tmp.Add(_inv[u]);
                tmp.Sort();
                for (int k = 0; k < tmp.Count; k++) _rows[_colStart[j] + k] = tmp[k];
            }

            // Row lists (transpose of the column structure).
            var rowCount = new int[n + 1];
            for (int p = 0; p < _rows.Length; p++) rowCount[_rows[p] + 1]++;
            for (int i = 0; i < n; i++) rowCount[i + 1] += rowCount[i];
            _rowStart = (int[])rowCount.Clone();
            _rowCols = new int[_rows.Length];
            _rowIdx = new int[_rows.Length];
            var fill = new int[n];
            for (int j = 0; j < n; j++)
                for (int p = _colStart[j]; p < _colStart[j + 1]; p++)
                {
                    int r = _rows[p];
                    int slot = _rowStart[r] + fill[r]++;
                    _rowCols[slot] = j;
                    _rowIdx[slot] = p;
                }

            // Lower part of the permuted A by column, pointing back into A.Values.
            var aCount = new int[n + 1];
            for (int i = 0; i < n; i++)
                for (int k = a.RowStart[i]; k < a.RowStart[i + 1]; k++)
                {
                    int r = _inv[i], c = _inv[a.Columns[k]];
                    if (r >= c) aCount[c + 1]++;
                }
            for (int j = 0; j < n; j++) aCount[j + 1] += aCount[j];
            _aColStart = (int[])aCount.Clone();
            _aRows = new int[aCount[n]];
            _aSrc = new int[aCount[n]];
            var aFill = new int[n];
            for (int i = 0; i < n; i++)
                for (int k = a.RowStart[i]; k < a.RowStart[i + 1]; k++)
                {
                    int r = _inv[i], c = _inv[a.Columns[k]];
                    if (r < c) continue;
                    int slot = _aColStart[c] + aFill[c]++;
                    _aRows[slot] = r;
                    _aSrc[slot] = k;
                }

            _d = new double[n];
            _work = new double[n];
            _y = new double[n];
        }

        /// <summary>Left-looking numeric LDLᵀ on the fixed structure.</summary>
        void Factor(SparseMatrix a)
        {
            int n = _n;
            for (int j = 0; j < n; j++)
            {
                for (int p = _aColStart[j]; p < _aColStart[j + 1]; p++) _work[_aRows[p]] += a.Values[_aSrc[p]];
                // Subtract contributions of earlier columns k with L[j,k] ≠ 0.
                for (int q = _rowStart[j]; q < _rowStart[j + 1]; q++)
                {
                    int k = _rowCols[q];
                    double ljk = _l[_rowIdx[q]];
                    double f = ljk * _d[k];
                    _work[j] -= ljk * f;
                    for (int p = _rowIdx[q] + 1; p < _colStart[k + 1]; p++) _work[_rows[p]] -= _l[p] * f;
                }
                double dj = _work[j];
                _work[j] = 0;
                _d[j] = dj;
                for (int p = _colStart[j]; p < _colStart[j + 1]; p++)
                {
                    int r = _rows[p];
                    _l[p] = _work[r] / dj;
                    _work[r] = 0;
                }
            }
        }
    }
}
