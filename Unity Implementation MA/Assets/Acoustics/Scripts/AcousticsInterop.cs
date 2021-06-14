// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using UnityEngine;

namespace Microsoft.Acoustics
{
    public class AcousticsInterop : IDisposable
    {
        private bool disposed = false;
        private System.IntPtr tritonHandle = IntPtr.Zero;

        public AcousticsInterop(bool debug)
        {
            AcousticsPAL.Triton_CreateInstance(debug, out tritonHandle);
            try
            {
                // Pass the triton instance to the Spatializer
                if (!AcousticsPAL.Spatializer_SetTritonHandle(tritonHandle))
                {
                    Debug.LogError("Failed to set Triton handle. Check your plugin configuration");
                }
            }
            catch(Exception)
            {
                Debug.LogError("Failed to set Triton handle on Project Acoustics Unity audio plugin. Check your plugin configuration");
            }
        }

        public AcousticsInterop(bool debug, string filename) : this(debug)
        {
            if (!AcousticsPAL.Triton_LoadAceFile(tritonHandle, filename))
            {
                throw new Exception("Invalid ACE file: " + filename);
            }

            try
            {
                AcousticsPAL.Spatializer_SetAceFileLoaded(true);
            }
            catch
            {
                Debug.LogError("Failed to set Triton state on Project Acoustics Unity audio plugin. Check your plugin configuration");
            }
        }

        ~AcousticsInterop()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed && tritonHandle != IntPtr.Zero)
            {
                // Tell Spatializer that Triton is going away.
                try
                {
                    AcousticsPAL.Spatializer_SetAceFileLoaded(false);
                    AcousticsPAL.Spatializer_SetTritonHandle(IntPtr.Zero);
                }
                catch
                {
                    Debug.LogError("Failed to dispose Triton data in Project Acoustics Unity audio plugin. Check your plugin configuration");
                }
                AcousticsPAL.Triton_UnloadAll(tritonHandle, true);
                AcousticsPAL.Triton_DestroyInstance(tritonHandle);
                tritonHandle = IntPtr.Zero;
                disposed = true;
            }
        }

        public int GetProbeCount()
        {
            int count = 0;
            if (!AcousticsPAL.Triton_GetProbeCount(tritonHandle, out count))
            {
                throw new InvalidOperationException();
            }

            return count;
        }

        public void GetProbeMetadata(int probeIndex, ref Vector3 location, out Color color)
        {
            var probeData = new AcousticsPAL.ProbeMetadata();
            if (!AcousticsPAL.Triton_GetProbeMetadata(tritonHandle, probeIndex, out probeData))
            {
                throw new InvalidOperationException();
            }

            location.x = probeData.Location.x;
            location.y = probeData.Location.y;
            location.z = probeData.Location.z;

            switch (probeData.State)
            {
                case AcousticsPAL.ProbeLoadState.DoesNotExist:
                case AcousticsPAL.ProbeLoadState.Invalid:
                    {
                        color = Color.red;
                        break;
                    }
                case AcousticsPAL.ProbeLoadState.Loaded:
                    {
                        color = Color.cyan;
                        break;
                    }
                case AcousticsPAL.ProbeLoadState.LoadFailed:
                    {
                        color = Color.magenta;
                        break;
                    }
                case AcousticsPAL.ProbeLoadState.LoadInProgress:
                    {
                        color = Color.yellow;
                        break;
                    }
                case AcousticsPAL.ProbeLoadState.NotLoaded:
                default:
                    {
                        color = Color.gray;
                        break;
                    }
            }
        }

        public void LoadProbes(Vector3 position, Vector3 probeLoadRegion)
        {
            bool unloadOutside = true;
            bool shouldBlock = false;
            int probeCount = 0;
            AcousticsPAL.Triton_LoadRegion(tritonHandle, new AcousticsPALPublic.TritonVec3f(position), new AcousticsPALPublic.TritonVec3f(probeLoadRegion), unloadOutside, shouldBlock, out probeCount);
        }

        public void LoadAllProbes()
        {
            AcousticsPAL.Triton_LoadAll(tritonHandle, true);
        }

        public VoxelMapSection GetVoxelMapSection(Vector3 minCorner, Vector3 maxCorner)
        {
            return new VoxelMapSection(tritonHandle, minCorner, maxCorner);
        }
        
        public void SetTransforms(Matrix4x4 worldToLocal, Matrix4x4 localToWorld)
        {
            try
            {
                AcousticsPAL.Spatializer_SetTransforms(new AcousticsPAL.ATKMatrix4x4(worldToLocal), new AcousticsPAL.ATKMatrix4x4(localToWorld));
            }
            catch
            {
                Debug.LogError("Failed to update Transform information on Project Acoustics Unity audio plugin. Check your plugin configuration.");
            }
        }
        
        public bool QueryAcoustics(Vector3 sourcePosition, Vector3 listenerPosition, out AcousticsPALPublic.TritonAcousticParameters acousticParams)
        {
            return AcousticsPAL.Triton_QueryAcoustics(tritonHandle, new AcousticsPALPublic.TritonVec3f(sourcePosition), new AcousticsPALPublic.TritonVec3f(listenerPosition), out acousticParams);
        }

        public bool GetOutdoornessAtListener(Vector3 listenerPosition, out float outdoorness)
        {
            return AcousticsPAL.Triton_GetOutdoornessAtListener(tritonHandle, new AcousticsPALPublic.TritonVec3f(listenerPosition), out outdoorness);
        }

        public Matrix4x4 GetPreappliedTransform()
        {
            // Get any pre-applied transform from ACE file
            // The resulting matrix is right-handed, row major
            AcousticsPAL.ATKMatrix4x4 rightHandedTx;
            AcousticsPAL.Triton_GetPreappliedTransform(tritonHandle, out rightHandedTx);

            // Convert right-handed to left-handed, and row-major to column-major
            Matrix4x4 leftHandedTx = new Matrix4x4();
            leftHandedTx.m00 = rightHandedTx.m11;
            leftHandedTx.m10 = rightHandedTx.m12;
            leftHandedTx.m20 = -rightHandedTx.m13;
            leftHandedTx.m30 = rightHandedTx.m14;
            leftHandedTx.m01 = rightHandedTx.m21;
            leftHandedTx.m11 = rightHandedTx.m22;
            leftHandedTx.m21 = -rightHandedTx.m23;
            leftHandedTx.m31 = rightHandedTx.m24;
            leftHandedTx.m02 = -rightHandedTx.m31;
            leftHandedTx.m12 = -rightHandedTx.m32;
            leftHandedTx.m22 = rightHandedTx.m33;
            leftHandedTx.m32 = -rightHandedTx.m34;
            leftHandedTx.m03 = rightHandedTx.m41;
            leftHandedTx.m13 = rightHandedTx.m42;
            leftHandedTx.m23 = -rightHandedTx.m43;
            leftHandedTx.m33 = rightHandedTx.m44;

            // Make sure the matrix we got is a valid transform matrix before returning
            if (leftHandedTx.ValidTRS())
            {
                return leftHandedTx;
            }
            else
            {
                // If the matrix wasn't valid, just return identity matrix in that case
                return Matrix4x4.identity;
            }
        }
    }
}