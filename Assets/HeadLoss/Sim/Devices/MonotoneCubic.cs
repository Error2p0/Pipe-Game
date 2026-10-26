using System;

namespace HeadLoss.Sim.Devices
{
    /// <summary>
    /// Monotone piecewise cubic Hermite interpolation (Fritsch &amp; Carlson 1980): no overshoot
    /// between data points. Outside the data range it extends linearly with the end segment's slope.
    /// </summary>
    public sealed class MonotoneCubic
    {
        readonly double[] _x, _y, _m;

        /// <param name="x">Strictly ascending abscissae (≥ 2 points).</param>
        /// <param name="y">Values.</param>
        public MonotoneCubic(double[] x, double[] y)
        {
            if (x.Length != y.Length || x.Length < 2) throw new ArgumentException("Need ≥ 2 matching points");
            _x = (double[])x.Clone();
            _y = (double[])y.Clone();
            int n = x.Length;
            var delta = new double[n - 1];
            for (int k = 0; k < n - 1; k++)
            {
                double h = x[k + 1] - x[k];
                if (!(h > 0)) throw new ArgumentException("x must be strictly ascending");
                delta[k] = (y[k + 1] - y[k]) / h;
            }
            _m = new double[n];
            _m[0] = delta[0];
            _m[n - 1] = delta[n - 2];
            for (int k = 1; k < n - 1; k++)
                _m[k] = delta[k - 1] * delta[k] <= 0 ? 0 : 0.5 * (delta[k - 1] + delta[k]);
            for (int k = 0; k < n - 1; k++)
            {
                if (delta[k] == 0) { _m[k] = 0; _m[k + 1] = 0; continue; }
                double a = _m[k] / delta[k];
                double b = _m[k + 1] / delta[k];
                if (a < 0) { _m[k] = 0; a = 0; }
                if (b < 0) { _m[k + 1] = 0; b = 0; }
                double s = a * a + b * b;
                if (s > 9)
                {
                    double tau = 3.0 / Math.Sqrt(s);
                    _m[k] = tau * a * delta[k];
                    _m[k + 1] = tau * b * delta[k];
                }
            }
        }

        public double MinX => _x[0];
        public double MaxX => _x[_x.Length - 1];

        /// <summary>Interpolated value.</summary>
        public double Evaluate(double x) => Evaluate(x, out _);

        /// <summary>Interpolated value and its derivative dy/dx.</summary>
        public double Evaluate(double x, out double derivative)
        {
            int n = _x.Length;
            if (x <= _x[0])
            {
                double s = (_y[1] - _y[0]) / (_x[1] - _x[0]);
                derivative = s;
                return _y[0] + s * (x - _x[0]);
            }
            if (x >= _x[n - 1])
            {
                double s = (_y[n - 1] - _y[n - 2]) / (_x[n - 1] - _x[n - 2]);
                derivative = s;
                return _y[n - 1] + s * (x - _x[n - 1]);
            }
            int k = 0;
            while (x > _x[k + 1]) k++;
            double h = _x[k + 1] - _x[k];
            double t = (x - _x[k]) / h;
            double t2 = t * t, t3 = t2 * t;
            double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
            double d00 = 6 * t2 - 6 * t, d10 = 3 * t2 - 4 * t + 1, d01 = -6 * t2 + 6 * t, d11 = 3 * t2 - 2 * t;
            derivative = (d00 * _y[k] + d10 * h * _m[k] + d01 * _y[k + 1] + d11 * h * _m[k + 1]) / h;
            return h00 * _y[k] + h10 * h * _m[k] + h01 * _y[k + 1] + h11 * h * _m[k + 1];
        }
    }
}
