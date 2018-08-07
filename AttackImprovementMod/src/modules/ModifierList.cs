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
            InitRangedModifiers( Settings.MeleeAccuracyFactors.Split( ',' ) );
            if ( MeleeModifiers.Count > 0 ) {
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
         public AttackModifier ( float modifier ) : this( "???", modifier ) {}
         public AttackModifier ( string name, float modifier ) {  DisplayName = name ?? "???"; Value = modifier; }
         public AttackModifier SetValue ( float modifier ) { Value = modifier; return this; }
         public AttackModifier SetName  ( string name ) { DisplayName = name ?? "???"; return this; }
         public AttackModifier SetName  ( string penalty, string bonus ) { DisplayName = Value >= 0 ? penalty : bonus; return this; }
      }

      private static List<Func<AttackModifier>> RangedModifiers, MeleeModifiers;
      private static CombatHUDTooltipHoverElement tip;
      private static string thisModifier;

      public  static ToHit Hit { get; private set; }
      public  static ICombatant They { get; private set; }
      public  static Mech Us { get; private set; }
      public  static MeleeAttackType AttackType { get; private set; }
      public  static Weapon AttackWeapon { get; private set; }
      public  static Vector3 AttackPos { get; private set; }
      public  static LineOfFireLevel LineOfFire { get; private set; }

      private static void SaveStates ( Mech attacker, ICombatant target, Weapon weapon, MeleeAttackType type, LineOfFireLevel lof ) {
         They = target;
         Us = attacker;
         AttackType = type;
         AttackWeapon = weapon;
         thisModifier = "(init)";
         LineOfFire = lof;
      }

      public static void RecordAttackPosition ( Vector3 attackPosition ) {
         AttackPos = attackPosition;
      }

      public static Func<AttackModifier> GetCommonModifierFactor ( string factorId ) {
         switch ( factorId ) {
         case "inspired":
            return () => new AttackModifier( "INSPIRED", Math.Min( 0f, Hit.GetAttackerAccuracyModifier( Us ) ) );

         case "refire":
            return () => new AttackModifier( "RE-ATTACK", Hit.GetRefireModifier( AttackWeapon ) );

         case "selfheat" :
            return () => new AttackModifier( "OVERHEAT", Hit.GetHeatModifier( Us ) );

         case "selfstoodup" :
            return () => new AttackModifier( "STOOD UP", Hit.GetStoodUpModifier( Us ) );

         case "selfwalked" :
            return () => new AttackModifier( "ATTACK AFTER MOVE", Hit.GetSelfSpeedModifier( Us ) );

         case "sensorimpaired":
            return () => new AttackModifier( "SENSOR IMPAIRED", Math.Max( 0f, Hit.GetAttackerAccuracyModifier( Us ) ) );

         case "sprint" :
            return () => new AttackModifier( "SPRINTED", Hit.GetSelfSprintedModifier( Us ) );

         case "targeteffect" :
            return () => new AttackModifier( "TARGET EFFECTS", Hit.GetEnemyEffectModifier( They ) );

         case "targetsize" :
            return () => new AttackModifier( "TARGET SIZE", Hit.GetTargetSizeModifier( They ) );

         case "targetterrain" :
            return () => new AttackModifier( "TARGET TERRAIN", Hit.GetTargetTerrainModifier( They, They.CurrentPosition, false ) );

         case "targetterrainmelee" : // Need to be different (an extra space) to avoid key collision
            return () => new AttackModifier( "TARGET TERRAIN ", Hit.GetTargetTerrainModifier( They, They.CurrentPosition, true ) );

         case "weaponaccuracy" :
            return () => new AttackModifier( "WEAPON ACCURACY", Hit.GetWeaponAccuracyModifier( Us, AttackWeapon ) );
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

      internal static void InitRangedModifiers ( string[] factors ) {
         RangedModifiers = new List<Func<AttackModifier>>();
         HashSet<string> Factors = new HashSet<string>();
         foreach ( string e in factors ) Factors.Add( e.Trim().ToLower() );
         foreach ( string e in Factors ) try {
            Func<AttackModifier> factor = GetMeleeModifierFactor( e ); // TODO
            if ( factor == null ) factor = GetCommonModifierFactor( e );
            if ( factor == null )
               Warn( "Unknown accuracy component \"{0}\"", e );
            else
               RangedModifiers.Add( factor );
         } catch ( Exception ex ) { Error( ex ); }
         Info( "Ranged modifiers: " + Join( ",", Factors ) );
      }

      // ============ Melee ============

      public static Func<AttackModifier> GetMeleeModifierFactor ( string factorId ) {
         switch ( factorId ) {
         case "armmounted":
            return () => { AttackModifier result = new AttackModifier( "PUNCHING ARM" );
               if ( AttackType == MeleeAttackType.DFA || They is Vehicle || They.IsProne ) return result;
               if ( Us.MechDef.Chassis.PunchesWithLeftArm ) {
                  if ( Us.IsLocationDestroyed( ChassisLocations.LeftArm ) ) return result;
               } else if ( Us.IsLocationDestroyed( ChassisLocations.RightArm ) ) return result;
               return result.SetValue( CombatConstants.ToHit.ToHitSelfArmMountedWeapon );
            };

         case "dfa":
            return () => new AttackModifier( "DEATH FROM ABOVE", Hit.GetDFAModifier( AttackType ) );

         case "height":
            return () => { AttackModifier result = new AttackModifier( "HEIGHT DIFF" );
               if ( AttackType == MeleeAttackType.DFA )
                  return result.SetValue( Hit.GetHeightModifier( Us.CurrentPosition.y, They.CurrentPosition.y ) );
               float diff = AttackPos.y - They.CurrentPosition.y;
               if ( Math.Abs( diff ) < HalfMaxMeleeVerticalOffset || ( diff < 0 && ! CombatConstants.ToHit.ToHitElevationApplyPenalties ) ) return result;
               float mod = CombatConstants.ToHit.ToHitElevationModifierPerLevel;
               return result.SetValue( diff <= 0 ? mod : -mod );
            };

         case "obstruction" :
            return () => new AttackModifier( "OBSTRUCTED", Hit.GetCoverModifier( Us, They, Combat.LOS.GetLineOfFire( Us, AttackPos, They, They.CurrentPosition, They.CurrentRotation, out Vector3 collision ) ) );

         case "selfchassis" :
            return () => new AttackModifier( Hit.GetMeleeChassisToHitModifier( Us, AttackType ) ).SetName( "CHASSIS PENALTY", "CHASSIS BONUS" );

         case "selfterrain" :
            return () => new AttackModifier( "TERRAIN", Hit.GetSelfTerrainModifier( AttackPos, false ) );

         case "targetevasion" :
            return () => { AttackModifier result = new AttackModifier( "TARGET MOVED" );
               if ( ! ( They is AbstractActor actor ) ) return result;
               return result.SetValue( Hit.GetEvasivePipsModifier( actor.EvasivePipsCurrent, AttackWeapon ) );
            };

         case "targetprone" :
            return () => new AttackModifier( "TARGET PRONE", Hit.GetTargetProneModifier( They, true ) );

         case "targetshutdown" :
            return () => new AttackModifier( "TARGET SHUTDOWN", Hit.GetTargetShutdownModifier( They, true ) );
         }
         return null;
      }

      internal static void InitMeleeModifiers ( string[] factors ) {
         MeleeModifiers = new List<Func<AttackModifier>>();
         HashSet<string> Factors = new HashSet<string>();
         foreach ( string e in factors ) Factors.Add( e.Trim().ToLower() );
         foreach ( string e in Factors ) try {
            Func<AttackModifier> factor = GetMeleeModifierFactor( e );
            if ( factor == null ) factor = GetCommonModifierFactor( e );
            if ( factor == null )
               Warn( "Unknown accuracy component \"{0}\"", e );
            else
               MeleeModifiers.Add( factor );
         } catch ( Exception ex ) { Error( ex ); }
         Info( "Melee and DFA modifiers: " + Join( ",", Factors ) );
      }

      private static MethodInfo contemplatingDFA;

      [ Harmony.HarmonyPriority( Harmony.Priority.Low ) ]
      public static bool OverrideMeleeToolTips ( CombatHUDWeaponSlot __instance, ICombatant target ) { try {
         CombatHUDWeaponSlot slot = __instance;
         tip = slot.ToolTipHoverElement;
         thisModifier = "(Init)";
         AttackPos = HUD.SelectionHandler.ActiveState.PreviewPos;
         bool isDFA = (bool) contemplatingDFA?.Invoke( slot, new object[]{ target } );
         SaveStates( HUD.SelectedActor as Mech, target, slot.DisplayedWeapon, isDFA ? MeleeAttackType.DFA : MeleeAttackType.Punch, default( LineOfFireLevel ) );
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
         Weapon weapon = ( meleeAttackType == MeleeAttackType.DFA ) ? attacker.DFAWeapon : attacker.MeleeWeapon;
         thisModifier = "(Init)";
         SaveStates( attacker, target, weapon, meleeAttackType, default( LineOfFireLevel ) );
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