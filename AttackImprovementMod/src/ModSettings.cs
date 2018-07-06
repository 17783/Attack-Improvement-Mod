﻿namespace Sheepy.AttackImprovementMod {

   public class ModSettings {

      /* Increase hit distribution precision for degrading called shots. Default true. Fix hit distribution bug on game ver 1.1.0 and below. */
      public bool FixHitDistribution = true;

      /* Show heat and stability number in selection panel (bottom left) and target panel (top).  Default true. */
      public bool ShowHeatAndStab = true;


      /// 
      /// Called Shots
      /// 

      /* Enable Vehicle Called Shot, which the game did not implement fully. Default true. */
      public bool FixVehicleCalledShot = true;

      /* Enable clustering effect for called shots against mechs. Default true. */
      public bool CalledShotUseClustering = true;

      /* Increase or decrease called shot multiplier against mech.  0 to disable called shot, 1 is original strength.
       * Default is 0.33 to counter the effect of CalledShotClusterStrength. */
      public float MechCalledShotMultiplier = 0.33f;

      /* Increase or decrease called shot multiplier against mech.  0 to disable called shot, 1 is original strength.
       * Default is 0.75 to balance vehicle's lower number of locations. */
      public float VehicleCalledShotMultiplier = 0.75f;

      /* Override called shot percentage display of mech locations to show modded shot distribution. Default true. */
      public bool ShowRealMechCalledShotChance = true;

      /* Override called shot percentage display of vehicle locations to show modded shot distribution. Default true. */
      public bool ShowRealVehicleCalledShotChance = true;

      /* Display chance to one decimal. Default false. If true, will also enables ShowRealMechCalledShotChance and ShowRealVehicleCalledShotChance. */
      public bool ShowDecimalCalledChance = false;

      /// 
      /// To Hit Bonus and Penalty
      /// 

      /* Allow bonus total modifier to increase hit chance. */
      public bool AllowBonusHitChance = true;

      /* Remove the 5% step of hit chance. Allow odd piloting stat to enjoy their 2.5% hit chance. */
      public bool UnlockHitChanceStep = true;

      /* Modify base hit chance. Negative to adjust everything down, positive to adjust everything up. */
      public float BaseHitChanceModifier = -0.05f;

      /* Max hit chance after all modifiers but before roll correction. */
      public float MaxFinalHitChance = 0.95f;

      /* Min hit chance after all modifiers but before roll correction. */
      public float MinFinalHitChance = 0.05f;

      /* Make hit chance modifier has diminishing return rather than simple add and subtract. */
      public bool DiminishingHitChanceModifier = true;

      /* Diminishing Bonus = 2-Base^(Bonus/Divisor), 2 - 0.75^(Bonus/6).
         Example: +3 Bonus @ 80% Base ToHit = 1.1 x 0.8 = 88% Hit */
      public float DiminishingBonusPowerBase = 0.8f;
      public float DiminishingBonusPowerDivisor = 6f;

      /* Diminishing Penalty = Base^(Penalty/Divisor), Default 0.85^(Penalty/1.6)
         Example: +6 Penalty @ 80% Base ToHit = 54% x 0.8 = 43% Hit */
      public float DiminishingPenaltyPowerBase = 0.85f;
      public float DiminishingPenaltyPowerDivisor = 1.5f;

      /// 
      /// To Hit Rolls Correction
      /// 

      /* Increase or decrease roll correction strength. 0 to disable roll correction, 1 is original strength, max is 2 for double strength.
       * Default is 0.5 for less correction. */
      public float RollCorrectionStrength = 0.5f;

      /* Set miss streak breaker threshold. Only attacks with hit rate above the threshold will add to streak breaker.
       * Default is 0.5, same as game default. Set to 1 to disable miss streak breaker. */
      public float MissStreakBreakerThreshold = 0.5f;

      /* Set miss streak breaker divider. Set to negative integer or zero to make it a (positive) constant %.
       * Otherwise, MissStreakBreakerThreshold is deduced from triggering attack's hit rate, then divided by this much, then added to streak breaker's chance modifier.
       * Default is 5, same as game default. */
      public float MissStreakBreakerDivider = 5f;

      /* Show adjusted hit chance in weapon panel, instead of original (fake) hit chance, before streak breaker. Default false. */
      public bool ShowRealWeaponHitChance = false;

      /* Show hit chance to one decimal in weapon panel. Default false. */
      public bool ShowDecimalHitChance = false;

      /// 
      /// Melee and DFA
      /// 

      /* Allow all possible melee attack positions. */
      public bool IncreaseMeleePositionChoice = true;

      /* Allow all possible melee attack positions. */
      public bool IncreaseDFAPositionChoice = true;

      /* Break the restriction that one must stay still to melee adjacent mech. */
      public bool UnlockMeleePositioning = true;

      /* Allow DFA called shot on vehicles *
      public bool AllowDFACalledShotVehicle = true;

      /// 
      /// Logging
      /// 

      /* Log attacker, weapon, hit roll, correction, location roll, location weights etc. to "BATTLETECH\Mods\FixHitLocation\log_roll.txt", for copy and paste to Excel.
       * Default disabled. */
      public bool LogHitRolls = false;

      /* If true, don't clear log on mod load (game launch). */
      public bool PersistentLog = false;

      /* Location of mod log and roll log. Default is "" which auto detect mod folder. Relative path would be relative to BATTLETECH exe. */
      public string LogFolder = "";
   }

}