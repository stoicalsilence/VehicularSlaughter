using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CarHealthMeter : MonoBehaviour
{
    public CarController car;

    public Image carHPMeterImage;
    private float displayedFill;
    public TextMeshProUGUI carHealthText;
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (car.currentVehicleHealth > 0)
        {
            float targetFill = (float)car.currentVehicleHealth / car.maxVehicleHealth;
            displayedFill = Mathf.Lerp(displayedFill, targetFill, Time.deltaTime * 10f);
            carHPMeterImage.fillAmount = displayedFill;

            carHealthText.text = (int)car.currentVehicleHealth + " / " + (int)car.maxVehicleHealth;
        }
    }
}
