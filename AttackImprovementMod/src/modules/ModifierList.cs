﻿using BattleTech.UI;
using BattleTech;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static System.Reflection.BindingFlags;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;

   public class ModifierList : BattleModModule {

      public override void CombatStartsOnce () {
         if ( Settings.RangedAccuracyFactors != null || Settings.MeleeAccuracyFactors != null )
            Patch( typeof( ToHit ), "GetToHitChance", "RecordAttackPosition", null );

         if ( Settings.RangedAccuracyFactors != null ) {
            InitRangedModifiers( Settings.RangedAccuracyFactors.Split( ',' ) );
            if ( RangedModifiers.Count > 0 ) {
               Patch( typeof( ToHit ), "GetAllModifiers", new Type[]{ typeof( AbstractActor ), typeof( Weapon ), typeof( ICombatant ), typeof( Vector3 ), typeof( Vector3 ), typeof( LineOfFireLevel ), typeof( bool ) }, "OverrideRangedModifiers", null );
               Patch( typeof( CombatHUDWeaponSlot ), "UpdateToolTipsFiring", NonPublic, typeof( ICombatant ), "OverrideRangedToolTips", null );
            }
         }
         if ( Settings.MeleeAccuracyFactors != null ) {
            InitMeleeModifiers( Settings.MeleeAccuracyFactors.Split( ',' ) );
            if ( MeleeModifiers.Count > 0 ) {
               contemplatingDFA = typeof( CombatHUDWeaponSlot ).GetMethod( "contemplatingDFA", NonPublic | Instance );
               if ( contemplatingDFA == null ) Warn( "CombatHUDWeaponSlot.contemplatingDFA not found, DFA will be regarded as normal melee." );
               Patch( typeof( ToHit ), "GetAllMeleeModifiers", new Type[]{ typeof( Mech ), typeof( ICombatant ), typeof( Vector3 ), typeof( MeleeAttackType ) }, "OverrideMeleeModifiers", null );
               Patch( typeof( CombatHUDWeaponSlot ), "UpdateToolTipsMelee", NonPublic, typeof( ICombatant ), "OverrideMeleeToolTips", null );
            }
         }
      }

      private static float HalfMaxMeleeVerticalOffset = 4f;

      public override void CombatStarts () {
         Hit = Combat.ToHit;
         MovementConstants con = CombatConstants.MoveConstants;
         HalfMaxMeleeVerticalOffset = con.MaxMeleeVerticalOffset / 2;
      }

      // ============ Common ============

      public struct AttackModifier {
         public string DisplayName;
         public float Value;
         public AttackModifier ( string name ) : this( name, 0f ) {}
         public AttackModifier ( float modifier = 0f ) : this( null, modifier ) {}
         public AttackModifier ( string name, float modifier ) {  DisplayName = name ?? "???"; Value = modifier; }
         public AttackModifier SetValue ( float modifier ) { Value = modifier; return this; }
         public AttackModifier SetName  ( string name ) { DisplayName = name ?? "???"; return this; }
         public AttackModifier SetName  ( string penalty, string bonus ) { DisplayName = Value >= 0 ? penalty : bonus; return this; }
      }

      private static List<Func<AttackModifier>> RangedModifiers, MeleeModifiers;
      private static CombatHUDTooltipHoverElement tip;
      private static string thisModifier;

      public  static ToHit Hit { get; private set; }
      public  static ICombatant Target { get; private set; }
      public  static AbstractActor Attacker { get; private set; }
      public  static Weapon AttackWeapon { get; private set; }
      public  static Vector3 AttackPos { get; private set; }
      public  static Vector3 TargetPos { get; private set; }

      private static void SaveStates ( AbstractActor attacker, ICombatant target, Weapon weapon ) {
         Target = target;
         Attacker = attacker;
         AttackWeapon = weapon;
         thisModifier = "(init)";
      }

      public static void RecordAttackPosition ( Vector3 attackPosition, Vector3 targetPosition ) {
         AttackPos = attackPosition;
         TargetPos = targetPosition;
      }

      internal static HashSet<string> InitModifiers ( List<Func<AttackModifier>> list, Func<string,Func<AttackModifier>> mapper, string[] factors ) {
         list = new List<Func<AttackModifier>>();
         HashSet<string> Factors = new HashSet<string>();
         foreach ( string e in factors ) Factors.Add( e.Trim().ToLower() );
         foreach ( string e in Factors ) try {
            Func<AttackModifier> factor = mapper( e );
            if ( factor == null ) factor = GetCommonModifierFactor( e );
            if ( factor == null )
               Warn( "Unknown accuracy component \"{0}\"", e );
            else
               list.Add( factor );
         } catch ( Exception ex ) { Error( ex ); }
         return Factors;
      }

      public static Func<AttackModifier> GetCommonModifierFactor ( string factorId ) {
         switch ( factorId ) {
         case "inspired":
            return () => new AttackModifier( "INSPIRED", Math.Min( 0f, Hit.GetAttackerAccuracyModifier( Attacker ) ) );

         case "refire":
            return () => new AttackModifier( "RE-ATTACK", Hit.GetRefireModifier( AttackWeapon ) );

         case "selfheat" :
            return () => new AttackModifier( "OVERHEAT", Hit.GetHeatModifier( Attacker ) );

         case "selfstoodup" :
            return () => new AttackModifier( "STOOD UP", Hit.GetStoodUpModifier( Attacker ) );

         case "selfterrain" :
            return () => new AttackModifier( "TERRAIN", Hit.GetSelfTerrainModifier( AttackPos, false ) );

         case "selfterrainmelee" :
            return () => new AttackModifier( "TERRAIN", Hit.GetSelfTerrainModifier( AttackPos, true ) );

         case "selfwalked" :
            return () => new AttackModifier( "ATTACK AFTER MOVE", Hit.GetSelfSpeedModifier( Attacker ) );

         case "sensorimpaired":
            return () => new AttackModifier( "SENSOR IMPAIRED", Math.Max( 0f, Hit.GetAttackerAccuracyModifier( Attacker ) ) );

         case "sprint" :
            return () => new AttackModifier( "SPRINTED", Hit.GetSelfSprintedModifier( Attacker ) );

         case "targeteffect" :
            return () => new AttackModifier( "TARGET EFFECTS", Hit.GetEnemyEffectModifier( Target ) );

         case "targetsize" :
            return () => new AttackModifier( "TARGET SIZE", Hit.GetTargetSizeModifier( Target ) );

         case "targetterrain" :
            return () => new AttackModifier( "TARGET TERRAIN", Hit.GetTargetTerrainModifier( Target, TargetPos, false ) );

         case "targetterrainmelee" : // Need to be different (an extra space) to avoid key collision
            return () => new AttackModifier( "TARGET TERRAIN ", Hit.GetTargetTerrainModifier( Target, TargetPos, true ) );

         case "weaponaccuracy" :
            return () => new AttackModifier( "WEAPON ACCURACY", Hit.GetWeaponAccuracyModifier( Attacker, AttackWeapon ) );
         }
         return null;
      }

      private static int AddToolTipDetail( AttackModifier tooltip ) {
         int mod = Mathf.RoundToInt( tooltip.Value );
         if ( mod == 0 ) return 0;
         if ( mod > 0 )
            tip.DebuffStrings.Add( tooltip.DisplayName + " +" + mod );
         else // if ( mod < 0 )
            tip.BuffStrings.Add( tooltip.DisplayName + " " + mod );
         return mod;
      }

      // ============ Ranged ============

      private static bool IsMoraleAttack;
      public  static LineOfFireLevel LineOfFire { get; private set; } // Ranged only. Do not use for melee

      internal static void InitRangedModifiers ( string[] factors ) {
         RangedModifiers = new List<Func<AttackModifier>>();
         HashSet<string> Factors = InitModifiers( RangedModifiers, GetRangedModifierFactor, factors );
         Info( "Ranged modifiers: " + Join( ",", Factors ) );
      }

      public static Func<AttackModifier> GetRangedModifierFactor ( string factorId ) {
         switch ( factorId ) {
         case "armmounted":
            return () => new AttackModifier( "ARM MOUNTED", Hit.GetSelfArmMountedModifier( AttackWeapon ) );

         case "range":
            return () => { 
               float modifier = Hit.GetRangeModifier( AttackWeapon, AttackPos, TargetPos );
               AttackModifier result = new AttackModifier( modifier );
      float range = Vector3.Distance( AttackPos, TargetPos );
      if ( range < AttackWeapon.MinRange ) return result.SetName( $"MIN RANGE (<{AttackWeapon.MinRange})" );
               if ( range < AttackWeapon.ShortRange ) return result.SetName( $"SHORT RANGE ({AttackWeapon.MinRange}-{AttackWeapon.ShortRange})" );
               if ( range < AttackWeapon.MediumRange ) return result.SetName( $"MEDIUM RANGE ({AttackWeapon.ShortRange}-{AttackWeapon.MediumRange})" );
               if ( range < AttackWeapon.LongRange ) return result.SetName( $"LONG RANGE ({AttackWeapon.MediumRange}-{AttackWeapon.LongRange})" );
               if ( range < AttackWeapon.MaxRange ) return result.SetName( $"MAX RANGE ({AttackWeapon.LongRange}-{AttackWeapon.MaxRange})" );
               return result.SetName( $"OUT OF RANGE ({AttackWeapon.MaxRange}+)" );
            };

         case "height":
            return () => new AttackModifier( "HEIGHT", Hit.GetHeightModifier( AttackPos.y, TargetPos.y ) );

         case "indirect" :
            return () => new AttackModifier( "INDIRECT", Hit.GetTargetDirectFireModifier( Target, LineOfFire < LineOfFireLevel.LOFObstructed && AttackWeapon.IndirectFireCapable ) );

         case "obstruction" :
            return () => new AttackModifier( "OBSTRUCTED", Hit.GetCoverModifier( Attacker, Target, LineOfFire ) );

         case "precisestrike":
            return () => new AttackModifier( CombatConstants.CombatUIConstants.MoraleAttackDescription.Name, Hit.GetMoraleAttackModifier( Target, IsMoraleAttack ) );

         case "targetevasion" :
            return () => new AttackModifier( "TARGET MOVED", Hit.GetTargetSpeedModifier( Target, AttackWeapon ) );

         case "targetprone" :
            return () => new AttackModifier( "TARGET PRONE", Hit.GetTargetProneModifier( Target, false ) );

         case "targetshutdown" :
            return () => new AttackModifier( "TARGET SHUTDOWN", Hit.GetTargetShutdownModifier( Target, false ) );
         }
         return null;
      }

      [ Harmony.HarmonyPriority( Harmony.Priority.Low ) ]
      public static bool OverrideRangedToolTips ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         CombatHUDWeaponSlot slot = __instance;
         tip = slot.ToolTipHoverElement;
         thisModifier = "(Init)";
         AttackPos = HUD.SelectionHandler.ActiveState.PreviewPos;
         LineOfFire = HUD.SelectionHandler.ActiveState.FiringPreview.GetPreviewInfo( target as AbstractActor ).LOFLevel;
         IsMoraleAttack = HUD.SelectionHandler.ActiveState.SelectionType == SelectionType.FireMorale;
         SaveStates( HUD.SelectedActor as Mech, target, slot.DisplayedWeapon );
         int TotalModifiers = 0;
         foreach ( var modifier in RangedModifiers ) {
            AttackModifier mod = modifier();
            thisModifier = mod.DisplayName;
            TotalModifiers += AddToolTipDetail( mod );
         }
         tip.BasicModifierInt = TotalModifiers; //Mathf.RoundToInt( Combat.ToHit.GetAllMeleeModifiers( us, they, they.CurrentPosition, attackType ) );
         return false;
      } catch ( Exception ex ) {
         // Reset before giving up
         tip?.DebuffStrings.Clear();
         tip?.BuffStrings.Clear();
         return Error( new ApplicationException( "Error in the ranged modifier *after* '" + thisModifier + "'", ex ) );
      } }

      [ Harmony.HarmonyPriority( Harmony.Priority.Low ) ]
      public static bool OverrideRangedModifiers ( ref float __result, AbstractActor attacker, Weapon weapon, ICombatant target, Vector3 attackPosition, Vector3 targetPosition, LineOfFireLevel lofLevel, bool isCalledShot ) { try {
         thisModifier = "(Init)";
         SaveStates( attacker, target, weapon );
         int modifiers = 0;
         foreach ( var modifier in RangedModifiers ) {
            AttackModifier mod = modifier();
            thisModifier = mod.DisplayName;
            modifiers += Mathf.RoundToInt( mod.Value );
         }
         if ( modifiers < 0 && ! CombatConstants.ResolutionConstants.AllowTotalNegativeModifier )
            modifiers = 0;
         __result = modifiers;
         return false;
      } catch ( Exception ex ) {
         return Error( new ApplicationException( "Error in the ranged modifier *after* '" + thisModifier + "'", ex ) );
      } }

      // ============ Melee ============
      
      private static MethodInfo contemplatingDFA;
      public  static MeleeAttackType AttackType { get; private set; }

      internal static void InitMeleeModifiers ( string[] factors ) {
         MeleeModifiers = new List<Func<AttackModifier>>();
         HashSet<string> Factors = InitModifiers( MeleeModifiers, GetMeleeModifierFactor, factors );
         Info( "Melee and DFA modifiers: " + Join( ",", Factors ) );
      }

      public static Func<AttackModifier> GetMeleeModifierFactor ( string factorId ) {
         switch ( factorId ) {
         case "armmounted":
            return () => { AttackModifier result = new AttackModifier( "PUNCHING ARM" );
               if ( AttackType == MeleeAttackType.DFA || Target is Vehicle || Target.IsProne || ! ( Attacker is Mech mech ) ) return result;
               if ( mech.MechDef.Chassis.PunchesWithLeftArm ) {
                  if ( mech.IsLocationDestroyed( ChassisLocations.LeftArm ) ) return result;
               } else if ( mech.IsLocationDestroyed( ChassisLocations.RightArm ) ) return result;
               return result.SetValue( CombatConstants.ToHit.ToHitSelfArmMountedWeapon );
            };

         case "dfa":
            return () => new AttackModifier( "DEATH FROM ABOVE", Hit.GetDFAModifier( AttackType ) );

         case "height":
            return () => { AttackModifier result = new AttackModifier( "HEIGHT DIFF" );
               if ( AttackType == MeleeAttackType.DFA )
                  return result.SetValue( Hit.GetHeightModifier( Attacker.CurrentPosition.y, Target.CurrentPosition.y ) );
               float diff = AttackPos.y - Target.CurrentPosition.y;
               if ( Math.Abs( diff ) < HalfMaxMeleeVerticalOffset || ( diff < 0 && ! CombatConstants.ToHit.ToHitElevationApplyPenalties ) ) return result;
               float mod = CombatConstants.ToHit.ToHitElevationModifierPerLevel;
               return result.SetValue( diff <= 0 ? mod : -mod );
            };

         case "obstruction" :
            return () => new AttackModifier( "OBSTRUCTED", Hit.GetCoverModifier( Attacker, Target, Combat.LOS.GetLineOfFire( Attacker, AttackPos, Target, TargetPos, Target.CurrentRotation, out Vector3 collision ) ) );

         case "selfchassis" :
            return () => new AttackModifier( Hit.GetMeleeChassisToHitModifier( Attacker, AttackType ) ).SetName( "CHASSIS PENALTY", "CHASSIS BONUS" );

         case "targetevasion" :
            return () => { AttackModifier result = new AttackModifier( "TARGET MOVED" );
               if ( ! ( Target is AbstractActor actor ) ) return result;
               return result.SetValue( Hit.GetEvasivePipsModifier( actor.EvasivePipsCurrent, AttackWeapon ) );
            };

         case "targetprone" :
            return () => new AttackModifier( "TARGET PRONE", Hit.GetTargetProneModifier( Target, true ) );

         case "targetshutdown" :
            return () => new AttackModifier( "TARGET SHUTDOWN", Hit.GetTargetShutdownModifier( Target, true ) );
         }
         return null;
      }

      [ Harmony.HarmonyPriority( Harmony.Priority.Low ) ]
      public static bool OverrideMeleeToolTips ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         CombatHUDWeaponSlot slot = __instance;
         tip = slot.ToolTipHoverElement;
         thisModifier = "(Init)";
         AttackPos = HUD.SelectionHandler.ActiveState.PreviewPos;
         bool isDFA = (bool) contemplatingDFA?.Invoke( slot, new object[]{ target } );
         AttackType = isDFA ? MeleeAttackType.DFA : MeleeAttackType.Punch;
         SaveStates( HUD.SelectedActor as Mech, target, slot.DisplayedWeapon );
         int TotalModifiers = 0;
         foreach ( var modifier in MeleeModifiers ) {
            AttackModifier mod = modifier();
            thisModifier = mod.DisplayName;
            TotalModifiers += AddToolTipDetail( mod );
         }
         tip.BasicModifierInt = TotalModifiers; //Mathf.RoundToInt( Combat.ToHit.GetAllMeleeModifiers( us, they, they.CurrentPosition, attackType ) );
         return false;
      } catch ( Exception ex ) {
         // Reset before giving up
         tip?.DebuffStrings.Clear();
         tip?.BuffStrings.Clear();
         return Error( new ApplicationException( "Error in the melee modifier *after* '" + thisModifier + "'", ex ) );
      } }

      [ Harmony.HarmonyPriority( Harmony.Priority.Low ) ]
      public static bool OverrideMeleeModifiers ( ref float __result, Mech attacker, ICombatant target, Vector3 targetPosition, MeleeAttackType meleeAttackType ) { try {
         AttackType = meleeAttackType;
         Weapon weapon = ( meleeAttackType == MeleeAttackType.DFA ) ? attacker.DFAWeapon : attacker.MeleeWeapon;
         thisModifier = "(Init)";
         SaveStates( attacker, target, weapon );
         int modifiers = 0;
         foreach ( var modifier in MeleeModifiers ) {
            AttackModifier mod = modifier();
            thisModifier = mod.DisplayName;
            modifiers += Mathf.RoundToInt( mod.Value );
         }
         if ( modifiers < 0 && ! CombatConstants.ResolutionConstants.AllowTotalNegativeModifier )
            modifiers = 0;
         __result = modifiers;
         return false;
      } catch ( Exception ex ) {
         return Error( new ApplicationException( "Error in the melee modifier *after* '" + thisModifier + "'", ex ) );
      } }
   }
}