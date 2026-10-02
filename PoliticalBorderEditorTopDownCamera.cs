using System;
using System.Reflection;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Applies a reversible north-up, vertical campaign-map camera while the
    /// border editor is active. MapCameraView is a Bannerlord 1.4.8 internal
    /// integration boundary, so missing members disable only this presentation
    /// aid and leave the native camera active. The source/runtime editor checks
    /// verify the reflected property contract.
    /// </summary>
    internal static class PoliticalBorderEditorTopDownCamera
    {
        private const float MinimumCameraDistance = 10f;
        private static object _view;
        private static Camera _camera;
        private static MatrixFrame _originalViewFrame;
        private static MatrixFrame _originalCameraFrame;
        private static float _originalBearing;
        private static float _originalCameraDistance;
        private static float _originalTargetCameraDistance;
        private static bool _captured;
        private static bool _disabledForSession;

        internal static void BeginSession()
        {
            _disabledForSession = false;
        }

        internal static void Apply(object mapScreen)
        {
            if (_disabledForSession || mapScreen == null) return;
            try
            {
                object view = GetProperty(mapScreen, "MapCameraView");
                if (view == null)
                    throw new MissingMemberException(
                        mapScreen.GetType().FullName, "MapCameraView");
                if (!_captured || !ReferenceEquals(_view, view))
                {
                    Restore();
                    Capture(view);
                }

                Vec3 target = GetValue<Vec3>(_view, "IdealCameraTarget");
                float distance = GetValue<float>(_view, "CameraDistance");
                if (distance < MinimumCameraDistance
                    || float.IsNaN(distance) || float.IsInfinity(distance))
                    distance = MinimumCameraDistance;
                Vec3 position = new Vec3(
                    target.x, target.y, target.z + distance, -1f);
                Vec3 northUp = Vec3.Forward;
                MatrixFrame frame = MatrixFrame.CreateLookAt(
                    position, target, northUp);
                SetValue(_view, "CameraBearing", 0f);
                SetValue(_view, "CameraFrame", frame);
                _camera.Frame = frame;
            }
            catch (Exception exception)
            {
                Restore();
                _disabledForSession = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Full 2D planning camera could not be applied; the native map camera remains active.",
                    exception);
            }
        }

        internal static void Restore()
        {
            if (!_captured) return;
            try
            {
                SetValue(_view, "CameraBearing", _originalBearing);
                SetValue(_view, "CameraDistance", _originalCameraDistance);
                SetValue(_view, "TargetCameraDistance", _originalTargetCameraDistance);
                SetValue(_view, "CameraFrame", _originalViewFrame);
                if (_camera != null) _camera.Frame = _originalCameraFrame;
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "The editor could not restore the captured campaign camera state.",
                    exception);
            }
            finally
            {
                _captured = false;
                _view = null;
                _camera = null;
            }
        }

        private static void Capture(object view)
        {
            Camera camera = GetValue<Camera>(view, "Camera");
            if (camera == null)
                throw new MissingMemberException(view.GetType().FullName, "Camera");
            _view = view;
            _camera = camera;
            _originalViewFrame = GetValue<MatrixFrame>(view, "CameraFrame");
            _originalCameraFrame = camera.Frame;
            _originalBearing = GetValue<float>(view, "CameraBearing");
            _originalCameraDistance = GetValue<float>(view, "CameraDistance");
            _originalTargetCameraDistance =
                GetValue<float>(view, "TargetCameraDistance");
            _captured = true;
            PoliticalBorderEditorDiagnostics.Info(
                "Full 2D planning camera captured at the current native zoom and set north-up.");
        }

        private static T GetValue<T>(object instance, string name)
        {
            object value = GetProperty(instance, name);
            if (!(value is T))
                throw new MissingMemberException(instance.GetType().FullName, name);
            return (T)value;
        }

        private static object GetProperty(object instance, string name)
        {
            PropertyInfo property = FindProperty(instance.GetType(), name);
            if (property == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            return property.GetValue(instance, null);
        }

        private static void SetValue(object instance, string name, object value)
        {
            PropertyInfo property = FindProperty(instance.GetType(), name);
            MethodInfo setter = property == null ? null : property.GetSetMethod(true);
            if (setter == null)
                throw new MissingMemberException(instance.GetType().FullName, name);
            setter.Invoke(instance, new[] { value });
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
    }
}
