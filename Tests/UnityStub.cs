// Headless stand-in for the one Unity type the planner core touches.
// This file lives OUTSIDE Assets/ so the Unity editor never compiles it (which would clash
// with the real UnityEngine.Transform). It exists only for the dotnet test harness.
namespace UnityEngine
{
    public class Transform { }
}
