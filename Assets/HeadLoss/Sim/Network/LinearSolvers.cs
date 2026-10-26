using System;
using System.Collections.Generic;

namespace HeadLoss.Sim.Network
{
    /// <summary>Symmetric matrix in CSR form (full storage, sorted columns per row).</summary>
    public sealed class SparseMatrix
    {
        public readonly int N;
        public readonly int[] RowStart;
        public readonly int[] Columns;
        public readonly double[] Values;
        /// <summary>Index into <see cref="Values"/> of each diagonal entry.</summary>
        public readonly int[] Diagonal;

        /// <param name="n">Size.</param>
        /// <param name="offDiagonalPairs">Pairs (i, j), i ≠ j; duplicates allowed.</param>
        public SparseMatrix(int n, List<(int, int)> offDiagonalPairs)
        {
            N = n;
            var rows = new List<int>[n];
            for (int i = 0; i < n; i++) rows[i] = new List<int> { i };
            foreach ((int i, int j) in offDiagonalPairs)
            {
                if (!rows[i].Contains(j)) rows[i].Add(j);
                if (!rows[j].Contains(i)) rows[j].Add(i);
            }
            RowStart = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                rows[i].Sort();
                RowStart[i + 1] = RowStart[i] + rows[i].Count;
            }
            Columns = new int[RowStart[n]];
            Values = new double[RowStart[n]];
            Diagonal = new int[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < rows[i].Count; k++)
                {
                    Columns[RowStart[i] + k] = rows[i][k];
                    if (rows[i][k] == i) Diagonal[i] = RowStart[i] + k;
                }
            }
        }

        /// <summary>Index into <see cref="Values"/> of entry (i, j), or −1.</summary>
        public int IndexOf(int i, int j)
        {
            int lo = RowStart[i], hi = RowStart[i + 1] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int c = Columns[mid];
                if (c == j) return mid;
                if (c < j) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        public void Clear() => Array.Clear(Values, 0, Values.Length);

        /// <summary>y = A·x.</summary>
        public void Multiply(double[] x, double[] y)
        {
            for (int i = 0; i < N; i++)
            {
                double s = 0;
                for (int k = RowStart[i]; k < RowStart[i + 1]; k++) s += Values[k] * x[Columns[k]];
                y[i] = s;
            }
        }
    }

    /// <summary>Solves A·x = b for a symmetric positive definite <see cref="SparseMatrix"/>.</summary>
    public interface ISparseSolver
    {
        /// <param name="x">Initial guess on entry (may be ignored), solution on exit.</param>
        /// <returns>Iterations used (1 for direct solvers).</returns>
        int Solve(SparseMatrix a, double[] b, double[] x);
    }

    /// <summary>Dense LDLᵀ factorisation. Reference for tests; O(n³).</summary>
    public sealed class DenseLdlSolver : ISparseSolver
    {
        public int Solve(SparseMatrix a, double[] b, double[] x)
        {
            int n = a.N;
            var m = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int k = a.RowStart[i]; k < a.RowStart[i + 1]; k++) m[i, a.Columns[k]] = a.Values[k];
            var d = new double[n];
            var l = new double[n, n];
            for (int j = 0; j < n; j++)
            {
                double s = m[j, j];
                for (int k = 0; k < j; k++) s -= l[j, k] * l[j, k] * d[k];
                d[j] = s;
                l[j, j] = 1;
                for (int i = j + 1; i < n; i++)
                {
                    double t = m[i, j];
                    for (int k = 0; k < j; k++) t -= l[i, k] * l[j, k] * d[k];
                    l[i, j] = t / d[j];
                }
            }
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= l[i, k] * y[k];
                y[i] = s;
            }
            for (int i = 0; i < n; i++) y[i] /= d[i];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = y[i];
                for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
                x[i] = s;
            }
            return 1;
        }
    }

    /// <summary>
    /// Preconditioned conjugate gradient with an incomplete Cholesky IC(0) preconditioner.
    /// Falls back to Jacobi if IC(0) breaks down. Deterministic (fixed operation order).
    /// </summary>
    public sealed class PcgSolver : ISparseSolver
    {
        readonly double _tolerance;
        readonly int _maxIterations;

        // Workspace reused between solves of the same size.
        double[] _r = Array.Empty<double>(), _z = Array.Empty<double>(), _p = Array.Empty<double>(), _q = Array.Empty<double>();
        double[] _l = Array.Empty<double>();
        int[] _lowerEnd = Array.Empty<int>();
        bool _jacobi;

        /// <param name="tolerance">Relative residual ‖r‖/‖b‖ to stop at.</param>
        public PcgSolver(double tolerance, int maxIterations)
        {
            _tolerance = tolerance;
            _maxIterations = maxIterations;
        }

        public int Solve(SparseMatrix a, double[] b, double[] x)
        {
            int n = a.N;
            if (_r.Length != n)
            {
                _r = new double[n]; _z = new double[n]; _p = new double[n]; _q = new double[n];
                _lowerEnd = new int[n];
            }
            if (_l.Length != a.Values.Length) _l = new double[a.Values.Length];
            Factor(a);

            double bNorm = 0;
            for (int i = 0; i < n; i++) bNorm += b[i] * b[i];
            bNorm = Math.Sqrt(bNorm);
            if (bNorm == 0)
            {
                Array.Clear(x, 0, n);
                return 0;
            }

            a.Multiply(x, _q);
            for (int i = 0; i < n; i++) _r[i] = b[i] - _q[i];
            Precondition(a, _r, _z);
            Array.Copy(_z, _p, n);
            double rz = Dot(_r, _z, n);
            int it;
            for (it = 0; it < _maxIterations; it++)
            {
                if (Math.Sqrt(Dot(_r, _r, n)) <= _tolerance * bNorm) break;
                a.Multiply(_p, _q);
                double pq = Dot(_p, _q, n);
                if (pq <= 0) break;
                double alpha = rz / pq;
                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * _p[i];
                    _r[i] -= alpha * _q[i];
                }
                Precondition(a, _r, _z);
                double rzNew = Dot(_r, _z, n);
                double beta = rzNew / rz;
                rz = rzNew;
                for (int i = 0; i < n; i++) _p[i] = _z[i] + beta * _p[i];
            }
            return it;
        }

        static double Dot(double[] u, double[] v, int n)
        {
            double s = 0;
            for (int i = 0; i < n; i++) s += u[i] * v[i];
            return s;
        }

        /// <summary>IC(0): L has the sparsity of the lower triangle of A; stored in <see cref="_l"/> at A's indices.</summary>
        void Factor(SparseMatrix a)
        {
            int n = a.N;
            _jacobi = false;
            for (int i = 0; i < n; i++)
            {
                int start = a.RowStart[i];
                int diag = a.Diagonal[i];
                _lowerEnd[i] = diag;
                for (int k = start; k < diag; k++)
                {
                    int j = a.Columns[k];
                    // s = a_ij − Σ_m L_im L_jm over common columns m < j
                    double s = a.Values[k];
                    int pi = start, pj = a.RowStart[j];
                    while (pi < k && pj < a.Diagonal[j])
                    {
                        int ci = a.Columns[pi], cj = a.Columns[pj];
                        if (ci == cj) { s -= _l[pi] * _l[pj]; pi++; pj++; }
                        else if (ci < cj) pi++;
                        else pj++;
                    }
                    _l[k] = s / _l[a.Diagonal[j]];
                }
                double d = a.Values[diag];
                for (int k = start; k < diag; k++) d -= _l[k] * _l[k];
                if (!(d > 0))
                {
                    _jacobi = true;
                    return;
                }
                _l[diag] = Math.Sqrt(d);
            }
        }

        void Precondition(SparseMatrix a, double[] r, double[] z)
        {
            int n = a.N;
            if (_jacobi)
            {
                for (int i = 0; i < n; i++) z[i] = r[i] / a.Values[a.Diagonal[i]];
                return;
            }
            // Forward: L y = r
            for (int i = 0; i < n; i++)
            {
                double s = r[i];
                for (int k = a.RowStart[i]; k < _lowerEnd[i]; k++) s -= _l[k] * z[a.Columns[k]];
                z[i] = s / _l[a.Diagonal[i]];
            }
            // Backward: Lᵀ z = y (row-oriented L, column sweep)
            for (int i = n - 1; i >= 0; i--)
            {
                z[i] /= _l[a.Diagonal[i]];
                double zi = z[i];
                for (int k = a.RowStart[i]; k < _lowerEnd[i]; k++) z[a.Columns[k]] -= _l[k] * zi;
            }
        }
    }
}
