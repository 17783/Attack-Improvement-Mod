using BattleTech;
using BattleTech.UI;
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;
   using static System.Reflection.BindingFlags;

   public class RollCorrection : BattleModModule {
      
      private static bool NoRollCorrection = false;
      private static bool TrueRNG = false;
      private static Dictionary<float, float> correctionCache;
      private static string WeaponHitChanceFormat = "{0:0}%";

      public override void CombatStartsOnce () {
         if ( BattleMod.FoundMod( "Battletech.realitymachina.NoCorrections", "NoCorrectedRoll.InitClass" ) ) {
            Logger.BTML_LOG.Warn( Mod.Name + " detected realitymachina's True RNG (NoCorrections) mod, roll correction and streak breaker disabled." );
            TrueRNG = true;
         }
         if ( BattleMod.FoundMod( "aa.battletech.realhitchance", "RealHitChance.Loader" ) ) {
            Logger.BTML_LOG.Warn( Mod.Name + " detected casualmods's Real Hit Chance mod, which should be REMOVED because it does not support AIM's features. Just remember to set AIM's ShowCorrectedHitChance to true!" );
            Settings.ShowCorrectedHitChance = true;
         }

         NoRollCorrection = Settings.RollCorrectionStrength == 0f;

         if ( ! NoRollCorrection && ! TrueRNG ) {
            if ( Settings.RollCorrectionStrength != 1f )
               Patch( typeof( AttackDirector.AttackSequence ), "GetCorrectedRoll", NonPublic, new Type[]{ typeof( float ), typeof( Team ) }, "OverrideRollCorrection", null );
            if ( Settings.ShowCorrectedHitChance ) {
               correctionCache = new Dictionary<float, float>(20);
               Patch( typeof( CombatHUDWeaponSlot ), "SetHitChance", typeof( float ), "ShowCorrectedHitChance", null );
            }
         } else if ( Settings.ShowCorrectedHitChance )
            Log( "ShowCorrectedHitChance auto-disabled because roll Correction is disabled." );

         if ( ( Settings.MissStreakBreakerThreshold != 0.5f || Settings.MissStreakBreakerDivider != 5f ) && ! TrueRNG ) {
            if ( Settings.MissStreakBreakerThreshold == 1f || Settings.MissStreakBreakerDivider == 0f )
               Patch( typeof( Team ), "ProcessRandomRoll", new Type[]{ typeof( float ), typeof( bool ) }, "BypassMissStreakBreaker", null );
            else {
               StreakBreakingValueProp = typeof( Team ).GetField( "streakBreakingValue", NonPublic | Instance );
               if ( StreakBreakingValueProp != null )
                  Patch( typeof( Team ), "ProcessRandomRoll", new Type[]{ typeof( float ), typeof( bool ) }, "OverrideMissStreakBreaker", null );
               else
                  Error( "Can't find Team.streakBreakingValue. Miss Streak Breaker cannot be patched. (Can instead try to disable it.)" );
            }
         }

         if ( Settings.HitChanceFormat != null )
            WeaponHitChanceFormat = Settings.HitChanceFormat;
         else if ( Settings.HitChanceStep == 0f && ! Settings.DiminishingHitChanceModifier )
            WeaponHitChanceFormat = "{0:0.#}%";

         bool HitChanceFormatChanged = Settings.HitChanceFormat != null || ( Settings.HitChanceStep == 0f && Settings.HitChanceFormat != "{0:0}%" );
         if ( HitChanceFormatChanged || Settings.ShowCorrectedHitChance || Settings.MinFinalHitChance < 0.05f || Settings.MaxFinalHitChance > 0.95f ) {
            HitChance = typeof( CombatHUDWeaponSlot ).GetMethod( "set_HitChance", Instance | NonPublic );
            Refresh = typeof( CombatHUDWeaponSlot ).GetMethod( "RefreshNonHighlighted", Instance | NonPublic );
            Patch( typeof( CombatHUDWeaponSlot ), "SetHitChance", typeof( float ), "OverrideDisplayedHitChance", null );
         }

         if ( NoRollCorrection )
            UseWeightedHitNumbersProp = typeof( AttackDirector.AttackSequence ).GetField( "UseWeightedHitNumbers", Static | NonPublic );
      }

      FieldInfo UseWeightedHitNumbersProp;

      public override void CombatStarts () {
         if ( NoRollCorrection ) {
            if ( UseWeightedHitNumbersProp != null )
               UseWeightedHitNumbersProp.SetValue( null, false );
            else
               Warn( "Cannot find AttackDirector.AttackSequence.UseWeightedHitNumbers. Roll correction not disabled." );
         } else if ( correctionCache != null )
            Log( "Combat starts with {0} reverse roll correction cached from previous battles.", correctionCache.Count );
      }

      // ============ UTILS ============

      public static float CorrectRoll ( float roll, float strength ) {
         strength /= 2;
         return (float)( (Math.Pow(1.6*roll-0.8,3)+0.5)*strength + roll*(1-strength) );
      }

      // A reverse algorithm of AttackDirector.GetCorrectedRoll
      internal static float ReverseRollCorrection ( float target, float strength ) {
         if ( strength == 0.0f ) return target;
         // Solving r for target = ((1.6r-0.8)^3+0.5)*(s/2)+r*(1-s/2)
         double t = target, t2 = t*t, s = strength, s2 = s*s, s3 = s2*s,
                a = 125 * Math.Sqrt( ( 13824*t2*s - 13824*t*s - 125*s3 + 750*s2 + 1956*s + 1000 ) / s ),
                b = a / ( 4096*Math.Pow( 6, 3d/2d )*s ) + ( 250*t - 125 ) / ( 1024 * s ),
                c = Math.Pow( b, 1d/3d );
         return c == 0 ? target : (float)( c + (125*s-250)/(1536*s*c) + 0.5 );
      }

      // ============ Fixes ============

      public static bool OverrideRealHitChance () { return false; }

      public static bool OverrideRollCorrection ( ref float __result, float roll, Team team ) { try {
         roll = CorrectRoll( roll, Settings.RollCorrectionStrength );
         if ( team != null )
            roll -= team.StreakBreakingValue;
         __result = roll;
         return false;
      }                 catch ( Exception ex ) { return Error( ex ); } }

      public static bool BypassMissStreakBreaker () {
         return false;
      }

      private static FieldInfo StreakBreakingValueProp = null;
      public static bool OverrideMissStreakBreaker ( Team __instance, float targetValue, bool succeeded ) { try {
         if ( succeeded ) {
            StreakBreakingValueProp.SetValue( __instance, 0f );

         } else if ( targetValue > Settings.MissStreakBreakerThreshold ) {
            float mod;
            if ( Settings.MissStreakBreakerDivider > 0 )
               mod = ( targetValue - Settings.MissStreakBreakerThreshold ) / Settings.MissStreakBreakerDivider;
            else
               mod = - Settings.MissStreakBreakerDivider;
            StreakBreakingValueProp.SetValue( __instance, __instance.StreakBreakingValue + mod );
         }
         return false;
      }                 catch ( Exception ex ) { return Error( ex ); } }

      [ HarmonyPriority( Priority.High ) ] // Above alexanderabramov's Real Hit Chance mod
      public static void ShowCorrectedHitChance ( ref float chance ) { try {
         chance = Mathf.Clamp( chance, 0f, 1f );
         if ( ! correctionCache.TryGetValue( chance, out float corrected ) )
            correctionCache.Add( chance, corrected = ReverseRollCorrection( chance, Settings.RollCorrectionStrength ) );
         chance = corrected;
      }                 catch ( Exception ex ) { Error( ex ); } }

      private static MethodInfo HitChance, Refresh;

      // Override the original code to remove accuracy cap on display, since correction or other settings can push it above 95%.
      [ HarmonyPriority( Priority.High ) ] // Above alexanderabramov's Real Hit Chance mod
      public static bool OverrideDisplayedHitChance ( CombatHUDWeaponSlot __instance, float chance ) { try {
         HitChance.Invoke( __instance, new object[]{ chance } );
         __instance.HitChanceText.text = string.Format( WeaponHitChanceFormat, Mathf.Clamp( chance * 100f, 0f, 100f ) );
         Refresh.Invoke( __instance, null );
         return false;
      }                 catch ( Exception ex ) { return Error( ex ); } }
   }
}