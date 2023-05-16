using BepInEx;
using BepInEx.IL2CPP;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace LethalAction
{
    [BepInPlugin("LethalAction.Igniz", "LethalAction", "1.0.0")]
    public class LAMain : BasePlugin
    {
        public static List<NewNode> PKBodies = new List<NewNode>();
        public static bool SkipNextDiscovery = false;
        public static Interactable lastItem;

        public override void Load()
        {
            Log.LogInfo("Plugin LethalAction is loaded!");
            var harmony = new Harmony("LethalAction");
            harmony.PatchAll();
        }
        //This is the meat of the mod where it actually murders the victim.
        public static void LethalAction(Actor victim)
        {
            //Debug.Log("Attack Attempt");
            if (victim != null)
            {
                //Debug.Log("CURRENT HEALTH: " + victim.currentHealth);
                //Debug.Log(lastItem);
                //Fists dont have an interactable or controller, but does have a MurderPreset, we have to make a new interactable.
                if (BioScreenController.Instance.selectedSlot.isStatic == FirstPersonItemController.InventorySlot.StaticSlot.fists)
                {
                    lastItem = new Interactable(new InteractablePreset());
                    lastItem.preset.weapon = InteriorControls._instance.fistsWeapon;
                }
                if (victim.isDead == false && victim.currentHealth <= 0 && lastItem != null)
                {
                    InteractionController._instance.SetIllegalActionActive(true);
                    //Debug.Log("Murder Begin");
                    //Debug.Log(victim);
                    //Debug.Log(victim.animationController.cit);
                    //Debug.Log(Player.Instance);
                    //Debug.Log(lastItem.name);
                    MurderMO MO = new MurderMO();
                    MurderController.Murder NewMurder = new MurderController.Murder(victim.animationController.cit, victim.animationController.cit, MurderController.Instance.murderPreset, MO);
                    NewMurder.weapon = lastItem;
                    NewMurder.weaponStr = NewMurder.weapon.name;
                    NewMurder.weaponID = NewMurder.weapon.id;
                    NewMurder.weaponPreset = lastItem.preset;
                    //The MurderWeapon Preset won't exist for thrown items, for now we use a placeholder. Without it, Monikers won't generate which breaks the code.
                    //The MurderWeapon Preset has a type option which tells time of murder dialog (Moniker) what type of weapon kill them (Blunt/Bladed/ETC)
                    if (NewMurder.weaponPreset.weapon == null)
                    {
                        NewMurder.weaponPreset.weapon = Resources.Load<MurderWeaponPreset>("data/weapons/bluntobjects/Dumbell");
                    }
                    //This part is meant to be future proof for Player guns if someone mods that in. 
                    if (NewMurder.weaponPreset.weapon.ammunition.Count > 0)
                    {
                        NewMurder.ammo = NewMurder.weaponPreset.weapon.ammunition[0].prefab.GetComponent<InteractableController>().interactable;
                    }
                    if (NewMurder.ammo != null)
                    {
                        NewMurder.ammoID = NewMurder.ammo.id;
                        NewMurder.ammoStr = NewMurder.ammo.name;
                        NewMurder.ammoPreset = NewMurder.ammo.preset;
                    }
                    else
                    {
                        NewMurder.ammoID = 0;
                        NewMurder.ammoStr = "";
                        NewMurder.ammoPreset = null;
                    }
                    NewMurder.victim = victim.animationController.cit;
                    NewMurder.victimID = victim.animationController.cit.humanID;
                    NewMurder.murderer = Player.Instance;
                    NewMurder.murdererID = Player.Instance.humanID;
                    NewMurder.location = victim.animationController.cit.currentGameLocation.thisAsAddress;
                    //Previously when I used to one hit kill enemies for testing their animations were funky, leaving in-case it needs to be re-added.
                    //victim.animationController.SetPauseAnimation(true);
                    //victim.animationController.SetRagdoll(true, true);
                    victim.animationController.cit.Murder(Player.Instance, true, NewMurder, lastItem);
                    if (NewMurder.victim == MurderController.Instance.currentMurderer)
                    {
                        MurderController.Instance.PickNewMurderer();
                        MurderController.Instance.PickNewVictim();
                    }
                    if (NewMurder.victim == MurderController.Instance.currentVictim)
                    {
                        MurderController.Instance.PickNewVictim();
                    }
                    PKBodies.Add(victim.currentNode);
                }
            }

        }
        //Unless the murderer murders in the same node (basically point on the map) it won't bring up a new case
        [HarmonyPatch(typeof(GameplayController), "NewMurderCaseNotify")]
        public class CaseNotifyPatch
        {
            public static bool Prefix(NewGameLocation newLocation)
            {
                foreach (var v in PKBodies)
                {
                    if (newLocation.nodes.Contains(v))
                    {
                        Debug.Log("contains Node");
                        foreach (var z in MurderController.Instance.activeMurders)
                        {
                            if (z.location == newLocation && z.murderer != Player.Instance)
                            {
                                Debug.Log("contains Node, but is also real murder");
                                return true;
                            }
                        }
                        return false;
                    }
                }
                return true;
            }
        }
        //Method for toggling game logs, patch itself borrowed from CitySize mod.
        /*
        [HarmonyPatch(typeof(MainMenuController), "OnChangeCityGenerationOption")]
        public class MainMenuController_OnChangeCityGenerationOption
        {
            public static void Postfix(MainMenuController __instance)
            {
                Game.Instance.printDebug = true;
                Game.Instance.debugPrintLevel = 2;
            }

        }
        */

        //This is to prevent the player from starting a new murder case themselves, the logic is calculated in the VictimSearch patch
        //Not really the optimal way to do this, please PR a better way if you can.
        [HarmonyPatch(typeof(MurderController), "OnVictimDiscovery")]
        public class VictimDiscovery
        {
            public static bool Prefix()
            {
                if (SkipNextDiscovery)
                {
                    SkipNextDiscovery = !SkipNextDiscovery;
                    return false;
                }
                return true;
            }
        }

        //Calculates if the body the player is searching is one they murdered. If it is, it skips the next CaseNotify that would add it as the active case.
        //We don't want the player to get a case for their own murders (Game only cares about the actual murderer anyways, so if you turned in your own name it wouldn't work)
        [HarmonyPatch(typeof(ActionController), "Search")]
        public class VictimSearch
        {
            public static void Prefix(Interactable what, NewNode where, Actor who)
            {
                if (who.isPlayer && what.isActor != null && what.isActor.isDead)
                {
                    foreach (var Murder in MurderController.Instance.activeMurders)
                    {
                        if (Murder.victim == what.isActor && Murder.murderer == Player.Instance)
                        {
                            SkipNextDiscovery = true;
                        }
                    }
                }
            }
        }

        //Thrown items do damage so we assign them as the lastItem (Player's murder weapon) for when they're used to kill.
        [HarmonyPatch(typeof(InteractableController), "DropThis")]
        public class DroppedItem
        {
            public static void Postfix(InteractableController __instance)
            {
                if (__instance != null && __instance.interactable != null)
                {
                    lastItem = __instance.interactable;
                }
            }
        }
        //For normal weapons this tells us what weapon was used to kill from the player if it was the last thing selected.
        [HarmonyPatch(typeof(BioScreenController), "SelectSlot")]
        public class LastSlot
        {
            public static void Postfix(FirstPersonItemController.InventorySlot newSlot)
            {
                if (newSlot != null && newSlot.GetInteractable() != null)
                {
                    lastItem = newSlot.GetInteractable();
                }
            }
        }
        //This is where our damage is calculated, the game won't calculate damage while they're stunned normally
        //It runs our murder method when they get down to 0 or less.
        [HarmonyPatch(typeof(Citizen), "RecieveDamage")]
        public class Damaged
        {
            public static void Postfix(Citizen __instance, float amount)
            {
              //  Debug.Log(amount + " " + "Amount of damage!");
                if (__instance.isStunned)
                {
                    __instance.SetHealth(__instance.currentHealth -= amount);
                }
                if (__instance.currentHealth <= 0)
                {
                    LethalAction(__instance);
                }
            }
        }
        //Somewhere in the SaveState it hates having multiple sets of murder classes, so we have to pretend our murder is solved to avoid loading issues
        [HarmonyPatch(typeof(SaveStateController), "CaptureSaveState")]
        public class FixSavesPre
        {
            public static void Prefix()
            {
                foreach (var murder in MurderController.Instance.activeMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        MurderController.Instance.inactiveMurders.Add(murder);
                        murder.state = MurderController.MurderState.solved;
                    }
                }
                foreach (var murder in MurderController.Instance.inactiveMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        MurderController.Instance.activeMurders.Remove(murder);
                    }
                }
            }
        }
        //This reverts the previous patch so that the game you're currently in will still be able to report the body.
        [HarmonyPatch(typeof(SaveStateController), "CaptureSaveState")]
        public class FixSavesPost
        {
            public static void Postfix()
            {
                foreach (var murder in MurderController.Instance.inactiveMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        MurderController.Instance.activeMurders.Add(murder);
                        murder.state = MurderController.MurderState.waitForLocation;
                    }
                }
                foreach (var murder in MurderController.Instance.activeMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        MurderController.Instance.inactiveMurders.Remove(murder);
                    }
                }
            }
        }
        //This patch runs after load to try and fix several potential issues with loading into a game.
        //Some of this may be pointless or redundant, but I'd rather that than errors.
        [HarmonyPatch(typeof(MainMenuController), "FadeMenu")]
        public class FixSavesLoad
        {
            public static void Postfix()
            {
                PKBodies.Clear();
                foreach (var murder in MurderController.Instance.inactiveMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        MurderController.Instance.activeMurders.Add(murder);
                        CityData.Instance.deadCitizensDirectory.Add(murder.victim);
                        murder.mo = new MurderMO();
                        murder.location = murder.victim.animationController.cit.currentGameLocation.thisAsAddress;
                        murder.state = MurderController.MurderState.waitForLocation;
                        murder.activeMurderItems = new Dictionary<JobPreset.JobTag, Interactable>();
                    }
                }
                foreach (var murder in MurderController.Instance.activeMurders)
                {
                    if (murder.murderer == Player.Instance)
                    {
                        PKBodies.Add(murder.victim.currentNode);
                        MurderController.Instance.inactiveMurders.Remove(murder);
                    }
                }
            }
        }
    }
}
