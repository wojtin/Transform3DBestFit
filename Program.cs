﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace WindowsFormsApp1
{
    static class Globals
    {
        // global int
        /*
        public static string path_nf;
        public static string path_f;
        public static string path_tra;
        public static Matrix<double> matrix_nf;
        public static Matrix<double> matrix_f;
        public static Matrix<double> matrix_tra;
        */
    }

    public class Transform3D
    {
        #region Fields

        public Matrix<double> actualsMatrix;
        public Matrix<double> nominalsMatrix;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates Transform3D object from two arrays, converts to Math.NET matrices and assigns to actualsMatrix and nominalsMatrix
        /// </summary>
        /// <param name="actuals"></param>
        /// <param name="nominals"></param>
        public Transform3D(double[,] actuals, double[,] nominals)
        {
            //convert array to Math.NET Matrices
            this.actualsMatrix = Matrix<double>.Build.DenseOfArray(actuals);
            this.nominalsMatrix = Matrix<double>.Build.DenseOfArray(nominals);
        }

        /// <summary>
        /// Creates Transform3D object from two Math.NET matrices and assigns to actualsMatrix and nominalsMatrix
        /// </summary>
        /// <param name="actuals"></param>
        /// <param name="nominals"></param>
        public Transform3D(Matrix<double> actuals, Matrix<double> nominals)
        {
            //convert array to Math.NET Matrices
            this.actualsMatrix = actuals;
            this.nominalsMatrix = nominals;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets and sets the 4X4 transform matrix (of type double[,])
        /// </summary>
        public double[,] TransformMatrix { get; set; }

        /// <summary>
        /// Gets and sets the Transform Vector of the form [XTrans, YTrans, ZTrans, ZRot, YRot, XRot] with rotations in radians
        /// </summary>
        public double[] Transform6DOFVector { get; set; }
        public double[] Transform6DOFVectorSeparate { get; set; }

        /// <summary>
        /// Gets and sets the Transform Vector in Siemens Convention of the form [XTransSiemens, YTransSiemens, ZTransSiemens, ZRotSiemens, YRotSiemens, XRotSiemens] with rotations in radians
        /// </summary>
        public double[] TransformSiemens6DOFVector { get; set; }
        public double[] TransformSiemens6DOFVectorSeparate { get; set; }

        /// <summary>
        /// Gets and sets the 4X4 Rotation matrix following the conventional matrix multiplication order -> Rz*Ry*Rx
        /// returns as an array of type double[,]
        /// </summary>
        public double[,] RotationMatrix { get; set; }

        /// <summary>
        /// Gets and sets the 4X4 Translation matrix (of type double[,])
        /// </summary>
        public double[,] TranslationMatrix { get; set; }

        /// <summary>
        /// Gets and sets the RMS of the error between nominal points and best-fit-transformed actual points
        /// </summary>
        public double ErrorRMS { get; set; }

        /// <summary>
        /// Gets and sets the determinant of the rotation matrix to determine presence of reflection between point sets (-1 -> reflection present, 1-> no reflection present)
        /// </summary>
        public double RotMatrixDeterminant { get; set; }


        #endregion

        #region Public methods

        /// <summary>
        /// Calculates the 4x4 transformation from actualsMatrix to nominalsMatrix by the Singular Value Decomposition method as well as its component Rotation/Translation 
        /// matrices, transform fit error, Rotation matrix determinant and Transform vector od the form [XTrans, YTrans, ZTrans, ZRot, YRot, XRot]   
        /// /// </summary>
        /// <param name="actualsMatrix"></param>
        /// <param name="nominalsMatrix"></param>
        /// <returns></returns>
        public bool CalcTransform(Matrix<double> actualsMatrix, Matrix<double> nominalsMatrix)
        {
            //check to see if actuals and nominals matrices are same dimension and that they have only 3 columns
            if (actualsMatrix.ColumnCount != nominalsMatrix.ColumnCount || actualsMatrix.RowCount != nominalsMatrix.RowCount || actualsMatrix.ColumnCount != 3)
            {
                if (actualsMatrix.ColumnCount != 3)
                {
                    Console.WriteLine("Incoming arrays have more than 3 columns (X,Y,Z)");
                }
                else
                {
                    Console.WriteLine("Dimensions of actuals and nominals don't match.");
                }

                //throw new ArgumentException(string.Format("Check that incomiang arrays have matching dimensions and only 3 columns."));
                return false;
            }
            else
            {
                //find centroids of pointCloudStart and pointCloudFinish
                Matrix<double> actualsCentroid = GetCentroid(actualsMatrix).ToRowMatrix();
                Matrix<double> nominalsCentroid = GetCentroid(nominalsMatrix).ToRowMatrix();

                //make centroid matrix with centroid repeated for every row of actuals/nominalsMatrix
                Matrix<double> actualsCentroidNxM = actualsCentroid;
                Matrix<double> nominalsCentroidNxM = nominalsCentroid;
                int count = 0;
                while (count < actualsMatrix.RowCount - 1)
                {
                    actualsCentroidNxM = actualsCentroidNxM.Stack(actualsCentroid);
                    nominalsCentroidNxM = nominalsCentroidNxM.Stack(nominalsCentroid);
                    count = count + 1;
                }

                //center pointCloudStart/pointCloudFinishMatrix on centroids
                Matrix<double> actualsMatrixCentered = actualsMatrix - actualsCentroidNxM;
                Matrix<double> nominalsMatrixCentered = nominalsMatrix - nominalsCentroidNxM;

                //calculate covariance matrix
                Matrix<double> covarianceMatrix = actualsMatrixCentered.Transpose() * nominalsMatrixCentered;

                //singular value decomposition:  http://en.wikipedia.org/wiki/Singular_value_decomposition
                var svd = covarianceMatrix.Svd();

                Matrix<double> svdU = svd.U;
                Matrix<double> svdS = svd.S.ToRowMatrix();
                Matrix<double> svdVt = svd.VT;

                //calculate rotation matrix of the form -> nominal_i = RotationMatrix * actual_i + TranslationComponentVector
                Matrix<double> rotationMatrix = svdVt.Transpose() * svdU.Transpose();

                //calclate determinant of rotation matrix
                RotMatrixDeterminant = rotationMatrix.Determinant();

                //Check rotation matrix for reflection
                if (RotMatrixDeterminant < 0)
                {
                    Console.WriteLine("Refelction Detected");

                    //If refelecction exists, multiply by correction matrix such that reflectionCorrection is similar to an Identity Matrix but with value [N,M] = -1
                    //See equation 23,24 at http://igl.ethz.ch/projects/ARAP/svd_rot.pdf
                    Matrix<double> reflectionCorrection = new DiagonalMatrix(3, 3, 1.0);
                    reflectionCorrection[2, 2] = -1.0;

                    //Python (not Matlab) code from http://nghiaho.com/?page_id=671 contridicts with equation 23,24 at http://igl.ethz.ch/projects/ARAP/svd_rot.pdf
                    //svdVt = svdVt.InsertRow(3, svdVt.Row(2) * -1).RemoveRow(2);

                    //recalculate rotation matrix
                    rotationMatrix = svdVt.Transpose() * reflectionCorrection * svdU.Transpose();
                }

                //calculate translation matrix  of the form -> nominal_i = RotationMatrix * actual_i + TranslationComponentVector
                Matrix<double> TranslationComponentVector = -rotationMatrix * actualsCentroid.Transpose() + nominalsCentroid.Transpose();

                //Transform matrix comprised of Rotation and Translation matrices such that -> nominal_i = RotationMatrix * actual_i + TranslationComponentVector
                //append translation to rotation and append last row (0,0,0,1) to make to matrix suitable for matrix multiplication (4x4)
                Matrix<double> transformMatrix = rotationMatrix.Append(TranslationComponentVector);
                transformMatrix = transformMatrix.InsertRow(3, new DenseVector(new[] { 0.0, 0.0, 0.0, 1.0 }));

                ////append 4th column (0,0,0) and 4th row (0,0,0,1) to make to matrix suitable for matrix multiplication (4x4) multiply rotation and translation matrices 
                rotationMatrix = rotationMatrix.InsertColumn(3, new DenseVector(new[] { 0.0, 0.0, 0.0 }));
                rotationMatrix = rotationMatrix.InsertRow(3, new DenseVector(new[] { 0.0, 0.0, 0.0, 1.0 }));

                //Build Translation Matrix suitable for matrix multiplication (of the form transformMatrix = rotationMatrix * translationMatrix) from TranslationComponentVector
                Matrix<double> translationMatrix = rotationMatrix.Inverse().Multiply(transformMatrix);

                //Error Calculation
                //Calcualte actuals' = tranformMatrix * actualsMatrix (made possible by first appending a column matrix of 1's to actuals)
                Matrix<double> columnVectorof1s = new DenseMatrix(actualsMatrix.RowCount, 1) + 1;
                Matrix<double> actualsMatrixNx4 = actualsMatrix.InsertColumn(3, columnVectorof1s.Column(0));
                Matrix<double> actualsMatrixPrime = (transformMatrix * actualsMatrixNx4.Transpose()).Transpose();
                Matrix<double> errorMatrix = actualsMatrixPrime.RemoveColumn(3) - nominalsMatrix;
                Matrix<double> errorMatrixSquared = errorMatrix.PointwiseMultiply(errorMatrix);
                double errorMatrixSum = errorMatrixSquared.ColumnAbsoluteSums().Sum();


                //Method outputs
                RotationMatrix = rotationMatrix.ToArray();
                TranslationMatrix = translationMatrix.ToArray();
                ErrorRMS = Math.Sqrt(errorMatrixSum / actualsMatrix.RowCount);
                TransformMatrix = transformMatrix.ToArray();
                Transform6DOFVector = TransformMatrixTo6DOFVector(transformMatrix);
                Transform6DOFVectorSeparate = TransformMatrixTo6DOFVectorSeparate(rotationMatrix, translationMatrix);
                TransformSiemens6DOFVector = TransformMatrixToSiemens6DOFVector(transformMatrix);
                TransformSiemens6DOFVectorSeparate = TransformMatrixToSiemens6DOFVectorSeparate(rotationMatrix, translationMatrix);

                return true;
            }
        }


        #endregion

        #region Private methods

        /// <summary>
        /// Returns the centroid of an NxM matrix
        /// </summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        private Vector<double> GetCentroid(Matrix<double> matrix)
        {
            return matrix.ColumnSums() / matrix.RowCount;
        }

        /// <summary>
        /// Returns the 6DoF transform vector of the form [XTrans, YTrans, ZTrans, ZRot, YRot, XRot] with rotations in radians
        /// </summary>
        /// <param name="TransformMatrix"></param>
        /// <returns></returns>
        public double[] TransformMatrixTo6DOFVector(Matrix<double> TransformMatrix)
        {
            Vector<double> tempVector = new DenseVector(6);

            //Set the X,Y, and Z translation components of the transform vector
            tempVector[0] = TransformMatrix[0, 3];  //X translation
            tempVector[1] = TransformMatrix[1, 3];  //Y translation
            tempVector[2] = TransformMatrix[2, 3];  //Z translation

            //Set the Z, Y, and X rotation components of the transform vector - see equation derivation at page 5 of http://www.usna.edu/Users/cs/taylor/courses/si475/class/3dkinematics.pdf
            double beta = Math.Atan2(-TransformMatrix[2, 0], Math.Sqrt(Math.Pow(TransformMatrix[0, 0], 2) + Math.Pow(TransformMatrix[1, 0], 2)));    //Y rotation - beta
            double gamma = Math.Atan2(TransformMatrix[1, 0] / Math.Cos(beta), TransformMatrix[0, 0] / Math.Cos(beta));   //Z rotation - gamma
            double alpha = Math.Atan2(TransformMatrix[2, 1] / Math.Cos(beta), TransformMatrix[2, 2] / Math.Cos(beta));   //X rotation - alpha

            tempVector[3] = gamma;      //Z rotation - kappa
            tempVector[4] = beta;       //Y rotation - phi
            tempVector[5] = alpha;      //X rotation - omega

            return tempVector.ToArray();

        }

        private double[] TransformMatrixTo6DOFVectorSeparate(Matrix<double> RotationMatrix, Matrix<double> TranslationMatrix)
        {
            Vector<double> tempVector = new DenseVector(6);

            //Set the X,Y, and Z translation components of the transform vector
            tempVector[0] = TranslationMatrix[0, 3];  //X translation
            tempVector[1] = TranslationMatrix[1, 3];  //Y translation
            tempVector[2] = TranslationMatrix[2, 3];  //Z translation

            //Set the Z, Y, and X rotation components of the transform vector - see equation derivation at page 5 of http://www.usna.edu/Users/cs/taylor/courses/si475/class/3dkinematics.pdf
            double beta = Math.Atan2(-RotationMatrix[2, 0], Math.Sqrt(Math.Pow(RotationMatrix[0, 0], 2) + Math.Pow(RotationMatrix[1, 0], 2)));    //Y rotation - beta
            double gamma = Math.Atan2(RotationMatrix[1, 0] / Math.Cos(beta), RotationMatrix[0, 0] / Math.Cos(beta));   //Z rotation - gamma
            double alpha = Math.Atan2(RotationMatrix[2, 1] / Math.Cos(beta), RotationMatrix[2, 2] / Math.Cos(beta));   //X rotation - alpha

            tempVector[3] = gamma;      //Z rotation - kappa
            tempVector[4] = beta;       //Y rotation - phi
            tempVector[5] = alpha;      //X rotation - omega

            return tempVector.ToArray();

        }

        /// <summary>
        /// Returns the 6DoF transform vector in Siemens Convention of the form [XTransSiemens, YTransSiemens, ZTransSiemens, ZRotSiemens, YRotSiemens, XRotSiemens] with rotations in radians
        /// </summary>
        /// <param name="RotationMatrix"></param>
        /// <param name="TranslationMatrix"></param>
        /// <returns></returns>
        private double[] TransformMatrixToSiemens6DOFVector(Matrix<double> TransformMatrix)
        {
            Vector<double> tempVector = new DenseVector(6);

            //Set the X,Y, and Z translation components of the transform vector for Siemens convention
            //Translation components in Siemens convention is simply the negative of the typical translation components
            tempVector[0] = -TransformMatrix[0, 3];  //X translation
            tempVector[1] = -TransformMatrix[1, 3];  //Y translation
            tempVector[2] = -TransformMatrix[2, 3];  //Z translation

            //Set the Z, Y, and X rotation components of the transform vector - see equation derivation at page 5 of http://www.usna.edu/Users/cs/taylor/courses/si475/class/3dkinematics.pdf
            //Siemens convention was determined experimentally.  To find the typical 3X3 rotation matrix (RotationMatrix in this class), the following matrix multiplication order is used -> Rz*Ry*Rx
            //To achieve the same rotation matrix with angles provided by Siemens MEAFrame, the following matrix multiplication must be used -> transpose(Rx)*transpose(Ry)*transpose(Rz)
            double beta = Math.Atan2(-TransformMatrix[0, 2], Math.Sqrt(Math.Pow(TransformMatrix[0, 0], 2) + Math.Pow(TransformMatrix[0, 1], 2)));    //Y rotation - beta
            double gamma = Math.Atan2(TransformMatrix[0, 1] / Math.Cos(beta), TransformMatrix[0, 0] / Math.Cos(beta));   //Z rotation - gamma
            double alpha = Math.Atan2(TransformMatrix[1, 2] / Math.Cos(beta), TransformMatrix[2, 2] / Math.Cos(beta));   //X rotation - alpha

            tempVector[3] = gamma;      //Z rotation - kappa
            tempVector[4] = beta;       //Y rotation - phi
            tempVector[5] = alpha;      //X rotation - omega

            return tempVector.ToArray();

        }
        private double[] TransformMatrixToSiemens6DOFVectorSeparate(Matrix<double> RotationMatrix, Matrix<double> TranslationMatrix)
        {
            Vector<double> tempVector = new DenseVector(6);

            //Set the X,Y, and Z translation components of the transform vector for Siemens convention
            //Translation components in Siemens convention is simply the negative of the typical translation components
            tempVector[0] = -TranslationMatrix[0, 3];  //X translation
            tempVector[1] = -TranslationMatrix[1, 3];  //Y translation
            tempVector[2] = -TranslationMatrix[2, 3];  //Z translation

            //Set the Z, Y, and X rotation components of the transform vector - see equation derivation at page 5 of http://www.usna.edu/Users/cs/taylor/courses/si475/class/3dkinematics.pdf
            //Siemens convention was determined experimentally.  To find the typical 3X3 rotation matrix (RotationMatrix in this class), the following matrix multiplication order is used -> Rz*Ry*Rx
            //To achieve the same rotation matrix with angles provided by Siemens MEAFrame, the following matrix multiplication must be used -> transpose(Rx)*transpose(Ry)*transpose(Rz)
            double beta = Math.Atan2(-RotationMatrix[0, 2], Math.Sqrt(Math.Pow(RotationMatrix[0, 0], 2) + Math.Pow(RotationMatrix[0, 1], 2)));    //Y rotation - beta
            double gamma = Math.Atan2(RotationMatrix[0, 1] / Math.Cos(beta), RotationMatrix[0, 0] / Math.Cos(beta));   //Z rotation - gamma
            double alpha = Math.Atan2(RotationMatrix[1, 2] / Math.Cos(beta), RotationMatrix[2, 2] / Math.Cos(beta));   //X rotation - alpha

            tempVector[3] = gamma;      //Z rotation - kappa
            tempVector[4] = beta;       //Y rotation - phi
            tempVector[5] = alpha;      //X rotation - omega

            return tempVector.ToArray();

        }

        #endregion
    }

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
