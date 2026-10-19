using UnityEngine;

namespace RuntimeTranslator
{
    public sealed class TranslationUpdate : MonoBehaviour
    {
        private void Awake() { Application.wantsToQuit += WantsToQuit; }
        private void Update() { TranslationManager.Tick(); }
        private bool WantsToQuit() { TranslationManager.Shutdown(); return true; }
        private void OnApplicationQuit() { TranslationManager.Shutdown(); }
        private void OnDestroy()
        {
            Application.wantsToQuit -= WantsToQuit;
            TranslationManager.Shutdown();
        }
    }
}