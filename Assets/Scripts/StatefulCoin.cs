public class StatefulCoin : MonoBehaviour, IStateComponent
{
    public int ID { get; set; } // Set this in Inspector or via a Manager
    private bool _isCollected = false;

    public List<StateFeature> GetFeatures() => new List<StateFeature> {
        new StateFeature { Type = FeatureType.Discrete, Value = _isCollected ? 1 : 0 }
    };
    
    public void Collect() => _isCollected = true;
}