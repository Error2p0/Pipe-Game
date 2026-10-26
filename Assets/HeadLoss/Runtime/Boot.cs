using UnityEngine;
using UnityEngine.SceneManagement;

namespace HeadLoss.Unity
{
    /// <summary>Boot scene: loads Main. The main menu (Continue, New city, Settings, Quit) arrives with saves.</summary>
    public sealed class Boot : MonoBehaviour
    {
        [SerializeField] string mainScene = "Main";

        void Start() => SceneManager.LoadScene(mainScene);
    }
}
