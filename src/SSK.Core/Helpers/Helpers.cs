using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSK.Core.Helpers
{

    #region helpers
    public class MathExt
    {
        public static double StdDev(IEnumerable<double> values)
        {
            double ret = 0;
            int count = values.Count();
            if (count > 1)
            {
                //Compute the Average
                double avg = values.Average();

                //Perform the Sum of (value-avg)^2
                double sum = values.Sum(d => (d - avg) * (d - avg));

                //Put it all together
                ret = Math.Sqrt(sum / count);
            }
            return ret;
        }
    }
    public class SavitzkyGolayFilter
    {
        private int SidePoints { get; set; }

        private Matrix<double> Coefficients { get; set; }

        public SavitzkyGolayFilter(int sidePoints, int polynomialOrder)
        {
            this.SidePoints = sidePoints;
            this.Design(polynomialOrder);
        }

        /// <summary>
        /// Smoothes the input samples.
        /// </summary>
        /// <param name="samples"></param>
        /// <returns></returns>
        public double[] Process(double[] samples)
        {
            int length = samples.Length;
            double[] output = new double[length];
            int frameSize = (this.SidePoints << 1) + 1;
            double[] frame = new double[frameSize];

            for (int i = 0; i <= this.SidePoints; ++i)
            {
                Array.Copy(samples, frame, frameSize);
                output[i] = this.Coefficients.Column(i).DotProduct(Vector<double>.Build.DenseOfArray(frame));
            }

            for (int n = this.SidePoints + 1; n < length - this.SidePoints; ++n)
            {
                Array.ConstrainedCopy(samples, n - this.SidePoints, frame, 0, frameSize);
                output[n] = this.Coefficients.Column(this.SidePoints + 1).DotProduct(Vector<double>.Build.DenseOfArray(frame));
            }

            for (int i = 0; i <= this.SidePoints; ++i)
            {
                Array.ConstrainedCopy(samples, length - (this.SidePoints << 1), frame, 0, this.SidePoints << 1);
                output[length - 1 - this.SidePoints + i] = this.Coefficients.Column(this.SidePoints + i).DotProduct(Vector<double>.Build.DenseOfArray(frame));
            }

            return output;
        }

        private void Design(int polynomialOrder)
        {
            double[,] a = new double[(this.SidePoints << 1) + 1, polynomialOrder + 1];

            for (int m = -this.SidePoints; m <= this.SidePoints; ++m)
            {
                for (int i = 0; i <= polynomialOrder; ++i)
                {
                    a[m + this.SidePoints, i] = Math.Pow(m, i);
                }
            }

            Matrix<double> s = Matrix<double>.Build.DenseOfArray(a);
            this.Coefficients = s.Multiply(s.TransposeThisAndMultiply(s).Inverse()).Multiply(s.Transpose());
        }
    }
    public class ConsoleHelper
    {
        public static EventHandler<string> PrintData;
        public static void Print(string Message)
        {
            PrintData?.Invoke(null, Message);
        }
        public static void Print(string Message, params string[] Param)
        {
            var Msg = string.Format(Message, Param);
            PrintData?.Invoke(null, Msg);
        }
    }
    #endregion
}
