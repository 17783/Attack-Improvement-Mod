using BattleTech.UI;
using BattleTech;
using Localize;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;

   public class UserInterfaceWeapons : BattleModModule {

      public override void CombatStartsOnce () {
         Type slotType = typeof( CombatHUDWeaponSlot );
         if ( Settings.ShowBaseHitchance ) {
            Patch( slotType, "UpdateToolTipsFiring", typeof( ICombatant ), "ShowBaseHitChance", null );
            Patch( slotType, "UpdateToolTipsMelee", typeof( ICombatant ), "ShowBaseMeleeChance", null );
         }
         if ( Settings.ShowNeutralRangeInBreakdown ) {
            rangedPenalty = ModifierList.GetRangedModifierFactor( "range" );
            Patch( slotType, "UpdateToolTipsFiring", typeof( ICombatant ), "ShowNeutralRange", null );
         }
         if ( Settings.ShowWeaponProp || Settings.WeaponRangeFormat != null )
            Patch( slotType, "GenerateToolTipStrings", null, "UpdateWeaponTooltip" );
         if ( Settings.ShowReducedWeaponDamage || Settings.AltKeyWeaponStability )
            Patch( slotType, "RefreshDisplayedWeapon", null, "UpdateWeaponDamage" );
         if ( Settings.ShowTotalWeaponDamage ) {
            Patch( typeof( CombatHUDWeaponPanel ), "ShowWeaponsUpTo", null, "ShowTotalDamageSlot" );
            Patch( typeof( CombatHUDWeaponPanel ), "RefreshDisplayedWeapons", "ResetTotalWeaponDamage", "ShowTotalWeaponDamage" );
         }
         if ( Settings.ShowReducedWeaponDamage || Settings.ShowTotalWeaponDamage ) {
            // Update damage numbers (and multi-target highlights) after all slots are in a correct state.
            Patch( typeof( SelectionStateFireMulti ), "SetTargetedCombatant", null, "RefreshTotalDamage" );
            Patch( typeof( CombatHUDWeaponSlot ), "OnPointerUp", null, "RefreshTotalDamage" );
         }
         if ( Settings.AltKeyWeaponStability )
            Patch( typeof( CombatSelectionHandler ), "ProcessInput", "ToggleStabilityDamage", null );
      }

      // ============ Mouseover Hint ============

      public static void ShowBaseHitChance ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         if ( HUD.SelectedActor is Mech mech ) {
            float baseChance = RollModifier.StepHitChance( Combat.ToHit.GetBaseToHitChance( mech ) ) * 100;
            __instance.ToolTipHoverElement.BuffStrings.Add( new Text( "{0} {1} = {2:0}%", Translate( Pilot.PILOTSTAT_GUNNERY ), mech.SkillGunnery, baseChance ) );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void ShowBaseMeleeChance ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         if ( HUD.SelectedActor is Mech mech ) {
            float baseChance = RollModifier.StepHitChance( Combat.ToHit.GetBaseMeleeToHitChance( mech ) ) * 100;
            __instance.ToolTipHoverElement.BuffStrings.Add( new Text( "{0} {1} = {2:0}%", Translate( Pilot.PILOTSTAT_PILOTING ), mech.SkillPiloting, baseChance ) );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      private static Func<ModifierList.AttackModifier> rangedPenalty;

      public static void ShowNeutralRange ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         ModifierList.AttackModifier range = rangedPenalty();
         if ( range.Value != 0 ) return;
         __instance.ToolTipHoverElement.BuffStrings.Add( new Text( range.DisplayName ) );
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void UpdateWeaponTooltip ( CombatHUDWeaponSlot __instance ) { try {
         Weapon weapon = __instance.DisplayedWeapon;
         List<Text> spec = __instance.ToolTipHoverElement?.WeaponStrings;
         if ( weapon == null || spec == null || spec.Count != 3 ) return;
         if ( Settings.ShowWeaponProp && ! string.IsNullOrEmpty( weapon.weaponDef.BonusValueA ) )
            spec[0] = new Text( string.IsNullOrEmpty( weapon.weaponDef.BonusValueB ) ? "{0}" : "{0}, {1}", weapon.weaponDef.BonusValueA, weapon.weaponDef.BonusValueB );
         if ( Settings.WeaponRangeFormat != null )
            spec[2] = new Text( Settings.WeaponRangeFormat, weapon.MinRange, weapon.ShortRange, weapon.MediumRange, weapon.LongRange, weapon.MaxRange );
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ Weapon Panel ============

      private static float TotalDamage, AverageDamage;
      private static CombatHUDWeaponSlot TotalSlot;

      private static void AddToTotalDamage ( float dmg, CombatHUDWeaponSlot slot ) {
         float chance = slot.HitChance, multiplied = dmg * slot.DisplayedWeapon.ShotsWhenFired;
         if ( chance <= 0 ) return; // Hit Chance is -999.9 if it can't fire at target (Method ClearHitChance)
         SelectionState state = HUD.SelectionHandler.ActiveState;
         if ( state is SelectionStateFireMulti multi
           && state.AllTargetedCombatants.Contains( HUD.SelectedTarget )
           && HUD.SelectedTarget != multi.GetSelectedTarget( slot.DisplayedWeapon ) ) return;
         TotalDamage += multiplied;
         AverageDamage += multiplied * chance;
      }

      public static void UpdateWeaponDamage ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         if ( target == null ) return;
         Weapon weapon = __instance.DisplayedWeapon;
         if ( weapon == null || weapon.Category == WeaponCategory.Melee || ! weapon.CanFire ) return;
         string text = null;
         if ( Settings.AltKeyFriendlyFire && ( Input.GetKey( KeyCode.LeftAlt ) || Input.GetKey( KeyCode.RightAlt ) ) ) {
            float dmg = weapon.Instability();
            if ( Settings.ShowReducedWeaponDamage && target is AbstractActor actor )
			      dmg *= actor.StatCollection.GetValue<float>("ReceivedInstabilityMultiplier") * actor.EntrenchedMultiplier;
            if ( weapon.IsEnabled ) AddToTotalDamage( dmg, __instance );
            text = "<#FFFF00>" + (int) dmg;
         } else {
            if ( ActiveState is SelectionStateFireMulti multi && __instance.TargetIndex < 0 ) return;
            Vector2 position = ActiveState?.PreviewPos ?? weapon.parent.CurrentPosition;
            float raw = weapon.DamagePerShotAdjusted(); // damage displayed by vanilla
            float dmg = weapon.DamagePerShotFromPosition( MeleeAttackType.NotSet, position, target ); // damage with all masks and reductions factored
            if ( weapon.IsEnabled ) AddToTotalDamage( dmg, __instance );
            if ( Math.Abs( raw - dmg ) < 0.01 ) return;
            text = ( (int) dmg ).ToString();
         }
         if ( weapon.HeatDamagePerShot > 0 )
            text = string.Format( HUD.WeaponPanel.HeatFormatString, text, Mathf.RoundToInt( weapon.HeatDamagePerShot ) );
         if ( weapon.ShotsWhenFired > 1 )
            text = string.Format( "{0} <#FFFFFF>(x{1})", text, weapon.ShotsWhenFired );
         Verbo( text );
         __instance.DamageText.SetText( text, new object[0] );
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void ShowTotalDamageSlot ( CombatHUDWeaponPanel __instance, int topIndex, List<CombatHUDWeaponSlot> ___WeaponSlots ) { try {
         TotalSlot = null;
         if ( topIndex <= 0 || topIndex >= ___WeaponSlots.Count || __instance.DisplayedActor == null || ! ( __instance.DisplayedActor is Mech mech ) ) return;
         TotalSlot = ___WeaponSlots[ topIndex ];
         TotalSlot.transform.parent.gameObject.SetActive( true );
         TotalSlot.DisplayedWeapon = null;
         TotalSlot.WeaponText.text = Translate( "Total" );
         TotalSlot.AmmoText.text = "";
         TotalSlot.MainImage.color = Color.clear;
         TotalSlot.ToggleButton.childImage.color = Color.clear;
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void ResetTotalWeaponDamage () {
         TotalDamage = AverageDamage = 0;
      }

      public static void ShowTotalWeaponDamage ( List<CombatHUDWeaponSlot> ___WeaponSlots ) { try {
         if ( TotalSlot == null ) return;
         if ( ! Settings.ShowReducedWeaponDamage ) {
            foreach ( CombatHUDWeaponSlot slot in ___WeaponSlots ) {
               Weapon w = slot.DisplayedWeapon;
               if ( w != null && w.IsEnabled && w.CanFire )
                  AddToTotalDamage( w.DamagePerShotAdjusted(), slot );
            }
         }
         TotalSlot.DamageText.text = ( (int) TotalDamage ).ToString();
         TotalSlot.HitChanceText.text = ( (int) AverageDamage ).ToString();
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void RefreshTotalDamage () {
         if ( ActiveState is SelectionStateFireMulti )
            HUD.WeaponPanel.RefreshDisplayedWeapons(); // Refresh weapon highlight AFTER HUD.SelectedTarget is updated.
      }

      private static bool ShowingStabilityDamage = false;

      public static void ToggleStabilityDamage () { try {
         bool AltPressed = Input.GetKey( KeyCode.LeftAlt ) || Input.GetKey( KeyCode.RightAlt );
         if ( ShowingStabilityDamage != AltPressed ) {
            ShowingStabilityDamage = AltPressed;
            HUD.WeaponPanel.RefreshDisplayedWeapons();
         }
      }                 catch ( Exception ex ) { Error( ex ); } }
   }
}