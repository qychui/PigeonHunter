
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class GameSync : UdonSharpBehaviour
{
    void Start()
    {
        
    }

    public override void OnPlayerTriggerStay(VRCPlayerApi player)
    {
        //TODO:

        base.OnPlayerTriggerStay(player);
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        base.OnPlayerJoined(player);
    }
}
