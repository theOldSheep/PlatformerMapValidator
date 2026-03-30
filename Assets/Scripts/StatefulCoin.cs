using System.Collections.Generic;
using UnityEngine;

public class StatefulCoin : MonoBehaviour, IStateComponent
{
    private bool _isCollected = false;

    public void Collect() {
        _isCollected = true;
        gameObject.SetActive(false);
    }

    private static List<IStateFeature> _cachedGameStateFeatures = new List<IStateFeature> {
        new StatePosVelFeature<float> {
            Type = PosVelType.PosX
        },
        new StatePosVelFeature<float> {
            Type = PosVelType.PosY
        },
        new StateFeature<bool> {
            CustomEncoding = (ignored) => 0
        },
    };
    public static List<IStateFeature> GetFeatures() => _cachedGameStateFeatures;
    public List<object> GetFeaturesRawValue() => new List<object> {
        transform.position.x,
        transform.position.y,
        _isCollected
    };
    public void RestoreFeaturesRawValue(List<object> rawValues)
    {
        transform.position = new Vector3((float)rawValues[0], (float)rawValues[1], transform.position.z);
        bool collected = (bool) rawValues[2];
        _isCollected = collected;
        gameObject.SetActive(!collected);
    }
}