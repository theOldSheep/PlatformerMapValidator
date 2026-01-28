using System.Collections.Generic;
using UnityEngine;

public class StatefulCoin : MonoBehaviour, IStateComponent
{
    // No ID property needed anymore
    private bool _isCollected = false;

    public List<StateFeature> GetFeatures() => new List<StateFeature> {
        new StateFeature { Type = FeatureType.Discrete, Value = _isCollected ? 1 : 0 }
    };
    
    public void Collect() => _isCollected = true;
}