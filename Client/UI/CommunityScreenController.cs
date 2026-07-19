using System;
using System.Threading.Tasks;
using UnityEngine;

namespace TheQuartermaster.Client.UI
{
    /// <summary>
    /// Simple GInterface486 implementation that activates/deactivates a GameObject.
    /// Used as the Controller for the cloned Community tab so Select()/Deselect() work natively.
    /// </summary>
    public class CommunityScreenController : GInterface486
    {
        private readonly GameObject _screenObject;

        public CommunityScreenController(GameObject screenObject)
        {
            _screenObject = screenObject;
        }

        public void Show()
        {
            if (_screenObject != null)
                _screenObject.SetActive(true);
        }

        public Task<bool> TryHide()
        {
            if (_screenObject != null)
                _screenObject.SetActive(false);
            return Task.FromResult(true);
        }
    }
}
