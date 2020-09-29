using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace SSK.Core.Models
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
}
