using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;

namespace andywiecko.BurstTriangulator.Tests.Runtime
{
    public class RetriangulateDemoTest
    {
        [UnityTest]
        public IEnumerator DemoTest()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path: "Packages/com.andywiecko.burst.triangulator/Tests/Runtime/RetriangulateDemoTest.unity", new(LoadSceneMode.Single));
            var demo = Object.FindAnyObjectByType<RetriangulateDemo>();

            foreach (RetriangulateDemo.Cases c in Enum.GetValues(typeof(RetriangulateDemo.Cases)))
            {
                demo.Case = c;
                demo.Retriangulate();
                yield return new WaitForSeconds(0.334f);
            }
        }
    }
}
#endif
