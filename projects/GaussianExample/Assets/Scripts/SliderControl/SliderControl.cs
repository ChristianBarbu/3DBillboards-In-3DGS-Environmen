using System;
using GaussianSplatting.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SliderControl : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private GaussianSplatRenderer gs;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private Dimmer dimmer;

    [SerializeField] private float lowerBound;
    [SerializeField] private float upperBound;
    

    enum Dimmer
    {
        dimmerSH0,
        dimmerSH1,
        dimmerSH2,
        dimmerSH3
    }
    
    private void Awake()
    {
        slider.onValueChanged.AddListener(value => ChangeGSValue(value));
    }

    private void ChangeGSValue(float value)
    {
        // The value is in the range [0,1], but needs to be mapped onto [0.6, 1.2]
        if (lowerBound >= upperBound)
            throw new ArgumentException("lowerBound needs to be smaller than upperBound.");
        switch (dimmer)
        {
            case Dimmer.dimmerSH0:
                gs.m_DimmerSH0 = value;
                break;
            case Dimmer.dimmerSH1:
                gs.m_DimmerSH1 = value;
                break;
            case Dimmer.dimmerSH2:
                gs.m_DimmerSH2 = value;
                break;
            case Dimmer.dimmerSH3:
                gs.m_DimmerSH3 = value;
                break;
            default:
                throw new ArgumentException("Choose a dimmer for this slider.");
        }
        valueText.text = "" + Math.Round(value, 3);
    }
    
}