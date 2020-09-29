using System;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.AutoML;
using System.IO;
using System.Collections.Generic;
using System.Data;
using System.Text.RegularExpressions;
using System.Text;
using MathNet.Numerics.LinearAlgebra;
using System.Linq;
using System.Reflection;

namespace BalitTanah.SoilSensingKit.Modelv3
{
    #region models
    public class ResultPrediction
    {
        public string ElementName { get; set; }
        public float ElementValue { get; set; }
    }
    public class ModelOutput
    {
        public float Score { get; set; }
    }
    public class ModelInput
    {
        [ColumnName("ElementValue"), LoadColumn(0)]
        public float ElementValue { get; set; }


        [LoadColumn(1, 154)]
        [VectorType(154)]
        public float[] FeatureVector;
    }
    #endregion
    class Program
    {
        private static MLContext mlContext = new MLContext(seed: 1);
        static void Main(string[] args)
        {
            string RawFilePath = GetAbsolutePath(@"..\..\..\Raw\updated_SSK.csv");
            string DatasetFolder = GetAbsolutePath(@"..\..\..\Dataset");
            string PreProcessFolder = GetAbsolutePath(@"..\..\..\Preprocess");
            string ModelFolder = GetAbsolutePath(@"..\..\..\MLModels");

            if (ConsoleHelper.PrintData == null)
                ConsoleHelper.PrintData += (object sender, string e) => { Console.WriteLine(e); };

            //ConsoleHelper.Print("Split raw file to separate csv file (element)");
            //SplitToMultipleCSV(RawFilePath, DatasetFolder);
            //ConsoleHelper.Print("Pre-process each csv file / element");
            //PreProcessed(DatasetFolder, PreProcessFolder);
            ConsoleHelper.Print("Start AutoML");
            DoAutoML(PreProcessFolder, ModelFolder);
            ConsoleHelper.Print("Selesai");
        }
        /// <summary>
        /// Data input yang dimasukkan dari array 0 - akhir itu nilai reflectance dari wave length: 2501.982414 sampe 1350.724346
        /// </summary>
        /// <param name="DataReflectance"></param>
        public static List<ResultPrediction> Predict(string ModelFolder, List<float> DataReflectance)
        {
            if (DataReflectance == null || DataReflectance.Count != 154) return null;
            var datas = new List<ResultPrediction>();

            // Create sample data to do a single prediction with it 
            ModelInput inputData = PreProcessData(DataReflectance);
            var fileCsvs = Directory.GetFiles(ModelFolder, "*.csv");
            foreach (var file in fileCsvs)
            {
                var modelName = Path.GetFileNameWithoutExtension(file);
                ITransformer mlModel = mlContext.Model.Load(GetAbsolutePath(file), out DataViewSchema inputSchema);
                var predEngine = mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(mlModel);

                // Try a single prediction
                ModelOutput predictionResult = predEngine.Predict(inputData);

                ConsoleHelper.Print($"Prediction [{modelName}] --> Predicted value: {predictionResult.Score}");
                datas.Add(new ResultPrediction() { ElementName = modelName, ElementValue = predictionResult.Score });
            }
            return datas;
        }

        /// <summary>
        /// Data input yang dimasukkan dari array 0 - akhir itu nilai reflectance dari wave length: 2501.982414 sampe 1350.724346
        /// </summary>
        /// <param name="DataReflectance"></param>
        /// <returns></returns>
        static ModelInput PreProcessData(List<float> DataReflectance)
        {
            try
            {
                var item = new ModelInput();
                //convert to absorbance

                for (int col = 0; col < DataReflectance.Count; col++)
                {
                    DataReflectance[col] = (float)Math.Log(1 / DataReflectance[col]);
                }


                //sav gol filter
                SavitzkyGolayFilter filter = new SavitzkyGolayFilter(11, 2);

                List<double> rowDatas = new List<double>();
                for (int col = 0; col < DataReflectance.Count; col++)
                {
                    rowDatas.Add(DataReflectance[col]);
                }
                var filteredRow = filter.Process(rowDatas.ToArray());
                for (int col = 0; col < DataReflectance.Count; col++)
                {
                    DataReflectance[col] = (float)filteredRow[col];
                }


                //SNV

                rowDatas = new List<double>();
                for (int col = 0; col < DataReflectance.Count; col++)
                {
                    rowDatas.Add(DataReflectance[col]);
                }
                var mean = rowDatas.Average();
                var stdDev = MathExt.StdDev(rowDatas);
                for (int col = 0; col < DataReflectance.Count; col++)
                {
                    DataReflectance[col] = (float)((DataReflectance[col] - mean) / stdDev);
                }

                item.FeatureVector = DataReflectance.ToArray();

                return item;
            }
            catch
            {
                return null;
            }
        }
        static void DoAutoML(string DatasetFolder, string ModelFolder, uint TrainDuration = 60)
        {
            if (!Directory.Exists(ModelFolder)) Directory.CreateDirectory(ModelFolder);
            var fileCsvs = Directory.GetFiles(DatasetFolder, "*.csv");
            foreach (var file in fileCsvs)
            {
                ConsoleHelper.Print($"Do AutoML for {Path.GetFileNameWithoutExtension(file)} in {TrainDuration} secs");
                IDataView trainingDataView = mlContext.Data.LoadFromTextFile<ModelInput>(
                                                 path: file,
                                                 hasHeader: false,
                                                 separatorChar: ',',
                                                 allowQuoting: true,
                                                 allowSparse: false);
                var split = mlContext.Data.TrainTestSplit(trainingDataView, testFraction: 0.25);
                var experiment = mlContext.Auto().CreateRegressionExperiment(maxExperimentTimeInSeconds: TrainDuration);

                var result = experiment.Execute(split.TrainSet, labelColumnName: "ElementValue");

                ConsoleHelper.Print($"Best Trainer:{result.BestRun.TrainerName}");

                PrintRegressionMetrics(result.BestRun.ValidationMetrics);

                // Save model
                SaveModel(mlContext, result.BestRun.Model, ModelFolder + "\\" + Path.GetFileNameWithoutExtension(file) + ".zip", trainingDataView.Schema);



                var testResults = result.BestRun.Model.Transform(split.TestSet);

                var trueValues = testResults.GetColumn<float>("ElementValue");
                var predictedValues = testResults.GetColumn<float>("Score");
            }
        }
        private static void Evaluate(MLContext mlContext, IDataView trainingDataView, IEstimator<ITransformer> trainingPipeline)
        {
            // Cross-Validate with single dataset (since we don't have two datasets, one for training and for evaluate)
            // in order to evaluate and get the model's accuracy metrics
            ConsoleHelper.Print("=============== Cross-validating to get model's accuracy metrics ===============");
            var crossValidationResults = mlContext.Regression.CrossValidate(trainingDataView, trainingPipeline, numberOfFolds: 5, labelColumnName: "ElementValue");
            PrintRegressionFoldsAverageMetrics(crossValidationResults);
        }
        public static void PrintRegressionFoldsAverageMetrics(IEnumerable<TrainCatalogBase.CrossValidationResult<RegressionMetrics>> crossValidationResults)
        {
            var L1 = crossValidationResults.Select(r => r.Metrics.MeanAbsoluteError);
            var L2 = crossValidationResults.Select(r => r.Metrics.MeanSquaredError);
            var RMS = crossValidationResults.Select(r => r.Metrics.RootMeanSquaredError);
            var lossFunction = crossValidationResults.Select(r => r.Metrics.LossFunction);
            var R2 = crossValidationResults.Select(r => r.Metrics.RSquared);

            ConsoleHelper.Print($"*************************************************************************************************************");
            ConsoleHelper.Print($"*       Metrics for Regression model      ");
            ConsoleHelper.Print($"*------------------------------------------------------------------------------------------------------------");
            ConsoleHelper.Print($"*       Average L1 Loss:       {L1.Average():0.###} ");
            ConsoleHelper.Print($"*       Average L2 Loss:       {L2.Average():0.###}  ");
            ConsoleHelper.Print($"*       Average RMS:           {RMS.Average():0.###}  ");
            ConsoleHelper.Print($"*       Average Loss Function: {lossFunction.Average():0.###}  ");
            ConsoleHelper.Print($"*       Average R-squared:     {R2.Average():0.###}  ");
            ConsoleHelper.Print($"*************************************************************************************************************");
        }
        public static void PrintRegressionMetrics(RegressionMetrics metrics)
        {
            ConsoleHelper.Print($"*************************************************");
            ConsoleHelper.Print($"*       Metrics for regression model      ");
            ConsoleHelper.Print($"*------------------------------------------------");
            ConsoleHelper.Print($"*       LossFn:        {metrics.LossFunction:0.##}");
            ConsoleHelper.Print($"*       R2 Score:      {metrics.RSquared:0.##}");
            ConsoleHelper.Print($"*       Absolute loss: {metrics.MeanAbsoluteError:#.##}");
            ConsoleHelper.Print($"*       Squared loss:  {metrics.MeanSquaredError:#.##}");
            ConsoleHelper.Print($"*       RMS loss:      {metrics.RootMeanSquaredError:#.##}");
            ConsoleHelper.Print($"*************************************************");
        }
        private static void SaveModel(MLContext mlContext, ITransformer mlModel, string modelRelativePath, DataViewSchema modelInputSchema)
        {
            // Save/persist the trained model to a .ZIP file
            ConsoleHelper.Print($"=============== Saving the model  ===============");
            mlContext.Model.Save(mlModel, modelInputSchema, GetAbsolutePath(modelRelativePath));
            ConsoleHelper.Print("The model is saved to {0}", GetAbsolutePath(modelRelativePath));
        }

        public static string GetAbsolutePath(string relativePath)
        {
            Type t = MethodBase.GetCurrentMethod().DeclaringType;
            FileInfo _dataRoot = new FileInfo(t.Assembly.Location);
            string assemblyFolderPath = _dataRoot.Directory.FullName;

            string fullPath = Path.Combine(assemblyFolderPath, relativePath);

            return fullPath;
        }
        static void PreProcessed(string TargetFolder, string PreProcessFolder)
        {
            if (!Directory.Exists(PreProcessFolder)) Directory.CreateDirectory(PreProcessFolder);
            var fileCsvs = Directory.GetFiles(TargetFolder, "*.csv");
            foreach (var file in fileCsvs)
            {
                var dt = ConvertCSVtoDataTable(file);
                //convert to absorbance
                foreach (DataRow dr in dt.Rows)
                {
                    for (int col = 1; col < dt.Columns.Count; col++)
                    {
                        dr[col] = Math.Log(1 / double.Parse(dr[col].ToString()));
                    }
                }
                dt.AcceptChanges();
                //sav gol filter
                SavitzkyGolayFilter filter = new SavitzkyGolayFilter(11, 2);
                foreach (DataRow dr in dt.Rows)
                {
                    List<double> rowDatas = new List<double>();
                    for (int col = 1; col < dt.Columns.Count; col++)
                    {
                        rowDatas.Add(double.Parse(dr[col].ToString()));
                    }
                    var filteredRow = filter.Process(rowDatas.ToArray());
                    for (int col = 1; col < dt.Columns.Count; col++)
                    {
                        dr[col] = filteredRow[col - 1];
                    }
                }
                dt.AcceptChanges();
                //SNV
                foreach (DataRow dr in dt.Rows)
                {
                    List<double> rowDatas = new List<double>();
                    for (int col = 1; col < dt.Columns.Count; col++)
                    {
                        rowDatas.Add(double.Parse(dr[col].ToString()));
                    }
                    var mean = rowDatas.Average();
                    var stdDev = MathExt.StdDev(rowDatas);
                    for (int col = 1; col < dt.Columns.Count; col++)
                    {
                        dr[col] = (double.Parse(dr[col].ToString()) - mean) / stdDev;
                    }
                }
                dt.AcceptChanges();
                //save to file
                {
                    var col = 0;
                    StringBuilder sb = new StringBuilder();
                    //print column name

                    sb.Append(dt.Columns[col].ColumnName);
                    for (int x = 1; x < dt.Columns.Count; x++)
                    {
                        sb.Append("," + dt.Columns[x].ColumnName);
                    }
                    sb.Append(Environment.NewLine);
                    //print data column
                    foreach (DataRow dr in dt.Rows)
                    {
                        sb.Append(dr[col].ToString());
                        for (int x = 1; x < dt.Columns.Count; x++)
                        {
                            sb.Append("," + dr[x].ToString());
                        }
                        sb.Append(Environment.NewLine);
                    }
                    var PathStr = PreProcessFolder + "\\" + dt.Columns[col].ColumnName + ".csv";
                    File.WriteAllText(PathStr, sb.ToString());
                    ConsoleHelper.Print($"{dt.Columns[col].ColumnName} - file generated.");
                }
            }
        }
        static void SplitToMultipleCSV(string FileName, string TargetFolder)
        {
            if (File.Exists(FileName))
            {
                var dt = ConvertCSVtoDataTable(FileName);
                if (!Directory.Exists(TargetFolder)) Directory.CreateDirectory(TargetFolder);
                var RemovedCols = new List<string>();
                for (int col = 25; col < dt.Columns.Count; col++)
                {
                    var ColName = double.Parse(dt.Columns[col].ColumnName);
                    if (!(1350 < ColName && ColName < 2510))
                    {
                        RemovedCols.Add(dt.Columns[col].ColumnName);
                    }
                }
                foreach (var str in RemovedCols)
                {
                    dt.Columns.Remove(str);
                }
                dt.AcceptChanges();

                for (int col = 4; col < 25; col++)
                {
                    StringBuilder sb = new StringBuilder();
                    //print column name

                    sb.Append(dt.Columns[col].ColumnName);
                    for (int x = 25; x < dt.Columns.Count; x++)
                    {
                        sb.Append("," + dt.Columns[x].ColumnName);
                    }
                    sb.Append(Environment.NewLine);
                    //print data column
                    foreach (DataRow dr in dt.Rows)
                    {
                        if (dr[col].ToString() == "NA") continue;
                        sb.Append(dr[col].ToString());
                        for (int x = 25; x < dt.Columns.Count; x++)
                        {
                            sb.Append("," + dr[x].ToString());
                        }
                        sb.Append(Environment.NewLine);
                    }
                    var PathStr = TargetFolder + "\\" + dt.Columns[col].ColumnName + ".csv";
                    File.WriteAllText(PathStr, sb.ToString());
                    ConsoleHelper.Print($"{ dt.Columns[col].ColumnName} csv generated");
                }


            }
        }
        public static DataTable ConvertCSVtoDataTable(string strFilePath)
        {
            StreamReader sr = new StreamReader(strFilePath);
            string[] headers = sr.ReadLine().Split(',');
            DataTable dt = new DataTable();
            foreach (string header in headers)
            {
                dt.Columns.Add(header);
            }
            while (!sr.EndOfStream)
            {
                string[] rows = Regex.Split(sr.ReadLine(), ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
                DataRow dr = dt.NewRow();
                for (int i = 0; i < headers.Length; i++)
                {
                    dr[i] = rows[i];
                }
                dt.Rows.Add(dr);
            }
            return dt;
        }
    }

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
