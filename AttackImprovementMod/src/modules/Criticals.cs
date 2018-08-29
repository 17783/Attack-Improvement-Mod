﻿using BattleTech;
using Harmony;
using Localize;
using System.Reflection;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;
   using static System.Reflection.BindingFlags;

   public class Criticals : BattleModModule {

      private static Type MechType = typeof( Mech );
      private static MethodInfo CheckForCritMethod;

      private static float ThroughArmorCritThreshold = 0, ThroughArmorCritThresholdPerc = 0, ThroughArmorBaseCritChance, ThroughArmorVarCritChance;

      public override void CombatStartsOnce () {
         Type[] ResolveParams = new Type[]{ typeof( WeaponHitInfo ), typeof( Weapon ), typeof( MeleeAttackType ) };
         MethodInfo ResolveWeaponDamage = MechType.GetMethod( "ResolveWeaponDamage", ResolveParams );

         if ( Settings.SkipCritingDeadMech )
            Patch( ResolveWeaponDamage, "Skip_BeatingDeadMech", null );

         if ( Settings.TurretCritMultiplier > 0 )
            Patch( typeof( Turret ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );
         if ( Settings.VehicleCritMultiplier > 0 )
            Patch( typeof( Vehicle ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );

         if ( Settings.ThroughArmorCritChanceZeroArmor > 0 && HasCheckForCrit() ) {
            if ( Settings.FixFullStructureCrit ) {
               Warn( "FullStructureCrit disabled because ThroughArmorCritical is enabled, meaning full structure can be crit'ed." );
               Settings.FixFullStructureCrit = false;
            }
            Patch( ResolveWeaponDamage, "AddThroughArmorCritical", null );
            Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", "Override_ConsolidateCriticalHitInfo", null );
            Patch( typeof( CritChanceRules ), "GetCritChance" , "GetAIMCritChance", null ); // Vanilla Crit Hijack
            //Patch( CheckForCritMethod, "AIMCheckMechCrit", null ); // Use AIM Crit System. Tested working.
            ThroughArmorBaseCritChance = (float) Settings.ThroughArmorCritChanceFullArmor;
            ThroughArmorVarCritChance = (float) Settings.ThroughArmorCritChanceZeroArmor - ThroughArmorBaseCritChance;
            if ( Settings.ThroughArmorCritThreshold > 1 )
               ThroughArmorCritThreshold = (float) Settings.ThroughArmorCritThreshold;
            else
               ThroughArmorCritThresholdPerc = (float)Settings.ThroughArmorCritThreshold;
            if ( Settings.ThroughArmorCritThreshold != 0 && ! Settings.CritFollowDamageTransfer )
               Warn( "Disabling CritFollowDamageTransfer may affect ThroughArmorCritThreshold calculation." );

         } else if ( Settings.FixFullStructureCrit ) {
            Patch( ResolveWeaponDamage, "RecordCritMech", "ClearCritMech" );
            Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", null, "RemoveFullStructureLocationsFromCritList" );
         }

         if ( Settings.CritFollowDamageTransfer ) {
            Patch( MechType, "TakeWeaponDamage", "RecordHitInfo", "ClearHitInfo" );
            Patch( MechType, "DamageLocation", NonPublic, "UpdateCritLocation", null );
         }
      }

      private static bool HasCheckForCrit () { try {
         if ( CheckForCritMethod != null ) return true;
         CheckForCritMethod = MechType.GetMethod( "CheckForCrit", NonPublic | Instance );
         if ( CheckForCritMethod == null ) Warn( "Mech.CheckForCrit not found. One or more crit features disabled." );
         return CheckForCritMethod != null;
      } catch ( Exception ex ) {
         Error( ex );
         return false;
      } }

      [ HarmonyPriority( Priority.High ) ]
      public static bool Skip_BeatingDeadMech ( Mech __instance ) {
         if ( __instance.IsFlaggedForDeath || __instance.IsDead ) return false;
         return true;
      }

      // ============ Generic Critical System Support ============

      public static void PublishMessage ( ICombatant unit, string message, object arg, FloatieMessage.MessageNature type ) {
         unit.Combat.MessageCenter.PublishMessage( new AddSequenceToStackMessage(
            new ShowActorInfoSequence( unit, new Text( message, new object[] { arg } ), type, true ) ) );
      }

      public static float GetWeaponDamage ( AIMCritInfo info ) {
         return GetWeaponDamage( info.target, info.hitInfo, info.weapon );
      }

      public static float GetWeaponDamage ( AbstractActor target, WeaponHitInfo hitInfo, Weapon weapon ) {
         float damage = weapon.parent == null ? weapon.DamagePerShot : weapon.DamagePerShotAdjusted( weapon.parent.occupiedDesignMask );
         AbstractActor attacker = Combat.FindActorByGUID( hitInfo.attackerId );
         LineOfFireLevel lineOfFireLevel = attacker.VisibilityCache.VisibilityToTarget( target ).LineOfFireLevel;
         return target.GetAdjustedDamage( damage, weapon.Category, target.occupiedDesignMask, lineOfFireLevel, false );
      }

      public static void ConsolidateDamage ( Dictionary<int,float> consolidated, WeaponHitInfo info, Func<float> damageFunc ) {
         consolidated.Clear();
         int i = 0, len = info.numberOfShots;
         if ( ThroughArmorCritThreshold != 0 || ThroughArmorCritThresholdPerc != 0 ) {
            float damage = damageFunc();
            for ( ; i < len ; i++ ) {
               int location = info.hitLocations[i];
               consolidated.TryGetValue( location, out float allDamage );
               allDamage += damage;
               consolidated[ location ] = allDamage;
            }
         } else {
            for ( ; i < len ; i++ )
               consolidated[ info.hitLocations[i] ] = 1;
         }
      }

      private static void ResolveWeaponDamage ( AIMCritInfo info ) { try {
         ConsolidateDamage( damages, info.hitInfo, () => GetWeaponDamage( info ) );
         armoured.Clear();
         damaged = new Dictionary<int, float>();
         //Verbo( "SplitCriticalHitInfo found {0} hit locations.", damages.Count );
         info.SplitConsolidatedDamage( damages, armoured, damaged );
         damages.Clear();
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ Generic Critical System Core ============

      public static void CheckForCrit ( AIMCritInfo critInfo ) { try {
         if ( critInfo?.weapon == null ) return;
         float critChance = critInfo.GetCritChance();
         if ( critChance > 0 ) {
            float[] randomFromCache = Combat.AttackDirector.GetRandomFromCache( critInfo.hitInfo, 2 );
            if ( randomFromCache[ 0 ] <= critChance ) {
               MechComponent componentInSlot = critInfo.FindComponentInSlot( randomFromCache[1] );
               if ( componentInSlot != null ) {
                  PlayCritAudio( critInfo );
                  PlayCritVisual( critInfo );
                  AttackDirector.AttackSequence attackSequence = Combat.AttackDirector.GetAttackSequence( critInfo.hitInfo.attackSequenceId );
                  if ( attackSequence != null )
                     attackSequence.FlagAttackScoredCrit( componentInSlot as Weapon, componentInSlot as AmmunitionBox );
                  ComponentDamageLevel componentDamageLevel = GetDegradedComponentLevel( critInfo );
                  componentInSlot.DamageComponent( critInfo.hitInfo, componentDamageLevel, true );
               }
            }
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static ComponentDamageLevel GetDegradedComponentLevel ( AIMCritInfo info ) {
         MechComponent component = info.component;
         ComponentDamageLevel componentDamageLevel = component.DamageLevel;
         if ( component is Weapon && componentDamageLevel == ComponentDamageLevel.Functional ) {
            componentDamageLevel = ComponentDamageLevel.Penalized;
            PublishMessage( info.target, "{0} CRIT", component.UIName, FloatieMessage.MessageNature.CriticalHit );
         } else if ( componentDamageLevel != ComponentDamageLevel.Destroyed ) {
            componentDamageLevel = ComponentDamageLevel.Destroyed;
            PublishMessage( info.target, "{0} DESTROYED", component.UIName, FloatieMessage.MessageNature.ComponentDestroyed );
         }
         return componentDamageLevel;
      }

      public static void PlayCritAudio ( AIMCritInfo info ) {
         GameRepresentation GameRep = info.target.GameRep;
         if ( GameRep == null ) return;
         if ( info.weapon.weaponRep != null && info.weapon.weaponRep.HasWeaponEffect )
            WwiseManager.SetSwitch<AudioSwitch_weapon_type>( info.weapon.weaponRep.WeaponEffect.weaponImpactType, GameRep.audioObject );
         else
            WwiseManager.SetSwitch<AudioSwitch_weapon_type>( AudioSwitch_weapon_type.laser_medium, GameRep.audioObject );
         WwiseManager.SetSwitch<AudioSwitch_surface_type>( AudioSwitch_surface_type.mech_critical_hit, GameRep.audioObject );
         WwiseManager.PostEvent<AudioEventList_impact>( AudioEventList_impact.impact_weapon, GameRep.audioObject, null, null );
         WwiseManager.PostEvent<AudioEventList_explosion>( AudioEventList_explosion.explosion_small, GameRep.audioObject, null, null );
      }

      public static void PlayCritVisual ( AIMCritInfo info ) {
         ICombatant target = info.target;
         if ( target.GameRep == null ) return;
         MechRepresentation MechRep = target.GameRep as MechRepresentation;
         MechComponent component = info.component;
         AmmunitionBox AmmoCrited = component as AmmunitionBox;
         Jumpjet jumpjetCrited = component as Jumpjet;
         HeatSinkDef heatsinkCrited = component.componentDef as HeatSinkDef;
         if ( target.team.LocalPlayerControlsTeam )
            AudioEventManager.PlayAudioEvent( "audioeventdef_musictriggers_combat", "critical_hit_friendly ", null, null );
         else if ( !target.team.IsFriendly( Combat.LocalPlayerTeam ) )
            AudioEventManager.PlayAudioEvent( "audioeventdef_musictriggers_combat", "critical_hit_enemy", null, null );
         if ( MechRep != null && jumpjetCrited == null && heatsinkCrited == null && AmmoCrited == null && component.DamageLevel > ComponentDamageLevel.Functional )
            MechRep.PlayComponentCritVFX( info.critLocation );
         if ( AmmoCrited != null && component.DamageLevel > ComponentDamageLevel.Functional )
            target.GameRep.PlayVFX( info.critLocation, Combat.Constants.VFXNames.componentDestruction_AmmoExplosion, true, Vector3.zero, true, -1f );
      }

      public abstract class AIMCritInfo {
         public AbstractActor target;
         public WeaponHitInfo hitInfo;
         public Weapon weapon;
         public int hitLocation; // Only used for through armour crit.  All vanilla logic should use critLocation
         public int critLocation;
         public MechComponent component;
         public AIMCritInfo( AbstractActor target, WeaponHitInfo hitInfo, Weapon weapon ) {
            this.target = target;
            this.hitInfo = hitInfo;
            this.weapon = weapon;
         }
         public abstract float GetCritChance();
         public abstract MechComponent FindComponentInSlot( float random );
         public abstract void SplitConsolidatedDamage( Dictionary<int,float> input, Dictionary<int,float> armoured, Dictionary<int,float> damaged );
      }

      public class AIMMechCritInfo : AIMCritInfo {
         public Mech Me { get => target as Mech; }
         public ArmorLocation HitArmour { get => (ArmorLocation) hitLocation; }
         public ChassisLocations CritChassis { get => (ChassisLocations) critLocation; }
         public AIMMechCritInfo ( Mech target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override float GetCritChance() {
            if ( HitArmour != ArmorLocation.None && HitArmour != ArmorLocation.Invalid ) {
               critLocation = (int) MechStructureRules.GetChassisLocationFromArmorLocation( HitArmour );
               if ( ( Me.GetCurrentArmor( HitArmour ) <= 0 || Me.GetCurrentStructure( CritChassis ) == Me.GetMaxStructure( CritChassis ) ) )
                  return GetThroughArmourCritChance( target, HitArmour, weapon );
            } else {
               if ( CritChassis == ChassisLocations.None ) return 0;
               ThroughArmor = null;
            }
            return Combat.CritChance.GetCritChance( Me, CritChassis, weapon, true );
         }

         public override MechComponent FindComponentInSlot( float random ) {
            float slotCount = Me.MechDef.GetChassisLocationDef( CritChassis ).InventorySlots;
            int slot = (int)(slotCount * random );
            return component = Me.GetComponentInSlot( CritChassis, slot );
         }

         public override void SplitConsolidatedDamage( Dictionary<int,float> input, Dictionary<int,float> armoured, Dictionary<int,float> damaged ) {
            foreach ( var damage in input ) {
               ArmorLocation armour = (ArmorLocation) damage.Key;
               if ( armour == ArmorLocation.None || armour == ArmorLocation.Invalid ) continue;
               ChassisLocations location = MechStructureRules.GetChassisLocationFromArmorLocation( armour );
               if ( Me.IsLocationDestroyed( location ) ) continue;
               if ( Me.GetCurrentArmor( armour ) <= 0 && Me.GetCurrentStructure( location ) < Me.GetMaxStructure( location ) )
                  damaged.Add( (int) armour, damage.Value );
               else if ( ( ThroughArmorCritThreshold == 0 && ThroughArmorCritThresholdPerc == 0 ) // No threshold
               /*const*/ || ( ThroughArmorCritThreshold > 0 && damage.Value > ThroughArmorCritThreshold )
               /*abs% */ || ( ThroughArmorCritThresholdPerc > 0 && damage.Value > ThroughArmorCritThresholdPerc * Me.GetMaxArmor( armour ) )
               /*curr%*/ || ( ThroughArmorCritThresholdPerc < 0 && damage.Value > ThroughArmorCritThresholdPerc * ( Me.GetCurrentArmor( armour ) + damage.Value ) ) )
                  armoured.Add( (int) armour, damage.Value );
               //else
               //   Verbo( "{0} damage ({1}) on {2} not reach threshold {3} & {4}%", armour, damage.Value, mech.DisplayName, ThroughArmorCritThreshold, ThroughArmorCritThresholdPerc*100 );
            }

         }
      }

      public class AIMVehicleInfo : AIMCritInfo {
         public Vehicle Me { get => target as Vehicle; }
         public AIMVehicleInfo ( Vehicle target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}
         public override float GetCritChance() {
            return 1;
         }
         public override MechComponent FindComponentInSlot( float random ) {
            float slotCount = Me.allComponents.Count;
            int slot = (int)(slotCount * random );
            return component = Me.allComponents[ slot ];
         }
         public override void SplitConsolidatedDamage( Dictionary<int,float> input, Dictionary<int,float> armoured, Dictionary<int,float> damaged ) {
            foreach ( var pair in input )
               damaged.Add( pair.Key, pair.Value );
         }
      }

      public class AIMTurretInfo : AIMCritInfo {
         public Turret Me { get => target as Turret; }
         public AIMTurretInfo ( Turret target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}
         public override float GetCritChance() {
            return 1;
         }
         public override MechComponent FindComponentInSlot( float random ) {
            float slotCount = Me.allComponents.Count;
            int slot = (int)(slotCount * random );
            return component = Me.allComponents[ slot ];
         }
         public override void SplitConsolidatedDamage( Dictionary<int,float> input, Dictionary<int,float> armoured, Dictionary<int,float> damaged ) {
            foreach ( var pair in input )
               damaged.Add( pair.Key, pair.Value );
         }
      }

      // ============ Generic Crit Patch ============

      public static void EnableNonMechCrit ( AbstractActor __instance, WeaponHitInfo hitInfo ) { try {
         AIMCritInfo info = null;
         AttackDirector.AttackSequence attackSequence = Combat.AttackDirector.GetAttackSequence( hitInfo.attackSequenceId );
         Weapon weapon = attackSequence.GetWeapon( hitInfo.attackGroupIndex, hitInfo.attackWeaponIndex );
         //MeleeAttackType meleeAttackType = attackSequence.meleeAttackType;
         if ( __instance is Vehicle vehicle ) info = new AIMVehicleInfo( vehicle, hitInfo, weapon );
         else if ( __instance is Turret turret ) info = new AIMTurretInfo( turret, hitInfo, weapon );
         else return;
         ResolveWeaponDamage( info );
         foreach ( var damagedLocation in damaged ) {
            info.hitLocation = damagedLocation.Key;
            CheckForCrit( info );
         }
         ThroughArmor = null;
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static bool AIMCheckMechCrit ( Mech __instance, WeaponHitInfo hitInfo, ChassisLocations location, Weapon weapon ) {
         AIMMechCritInfo info = new AIMMechCritInfo( __instance, hitInfo, weapon ){
            hitLocation = (int) ThroughArmor.GetValueOrDefault(),
            critLocation = (int) location };
         CheckForCrit( info );
         return false;
      }

      // ============ ThroughArmorCritical ============

      private static Dictionary<int, float> armoured = new Dictionary<int, float>(), damages = new Dictionary<int, float>(), damaged;
      private static ArmorLocation? ThroughArmor;

      public static bool Override_ConsolidateCriticalHitInfo ( ref Dictionary<int, float> __result ) {
         if ( damaged == null ) return true;
         //foreach ( int i in damaged.Keys ) Verbo( "Crit list: Damaged {0}", (ArmorLocation) i );
         __result = damaged; // Use the result from SplitCriticalHitInfo
         damaged = null;
         return false;
      }

      public static void AddThroughArmorCritical ( Mech __instance, WeaponHitInfo hitInfo, Weapon weapon, MeleeAttackType meleeAttackType ) { try {
         Mech mech = __instance;
         ResolveWeaponDamage( new AIMMechCritInfo( mech, hitInfo, weapon ) );
         if ( armoured == null || armoured.Count <= 0 ) return;

         foreach ( var damagedArmour in armoured ) {
            ThroughArmor = (ArmorLocation) damagedArmour.Key;
            CheckForCritMethod.Invoke( mech, new object[]{ hitInfo, MechStructureRules.GetChassisLocationFromArmorLocation( ThroughArmor.GetValueOrDefault() ), weapon } );
         }
         ThroughArmor = null;
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static bool GetAIMCritChance ( ref float __result, ICombatant target, ChassisLocations hitLocation, Weapon weapon, bool shouldLog = false ) {
         if ( ThroughArmor == null ) return true;
         __result = GetThroughArmourCritChance( target, ThroughArmor.GetValueOrDefault(), weapon );
         return false;
      }

      public static float GetThroughArmourCritChance ( ICombatant target, ArmorLocation hitLocation, Weapon weapon ) {
         if ( target.StatCollection.GetValue<bool>( "CriticalHitImmunity" ) ) return 0;
         float chance = 0, critMultiplier = 0;
         if ( target is Mech )
            chance = GetThroughArmourBaseCritChance( (Mech) target, hitLocation );
         if ( chance > 0 )
            //chance = Mathf.Max( change, CombatConstants.ResolutionConstants.MinCritChance ); // Min Chance does not apply to TAC
            critMultiplier = Combat.CritChance.GetCritMultiplier( target, weapon, true );
         float result = chance * critMultiplier;
         AttackLog.LogCritChance( result, MechStructureRules.GetChassisLocationFromArmorLocation( hitLocation ) );
         return result;
      }

      public static float GetThroughArmourBaseCritChance ( Mech target, ArmorLocation hitLocation ) {
         float result = ThroughArmorBaseCritChance, max = target.GetMaxArmor( hitLocation );
         if ( ThroughArmorVarCritChance > 0 ) {
            float curr = target.GetCurrentArmor( hitLocation ), armorPercentage = curr / max;
            result += ( 1f - armorPercentage ) * ThroughArmorVarCritChance;
         }
         AttackLog.LogThroughArmourCritChance( result, max );
         return result;
      }

      // ============ FixFullStructureCrit ============

      private static Mech thisCritMech;

      public static void RecordCritMech ( Mech __instance ) {
         thisCritMech = __instance;
      }

      public static void ClearCritMech () {
         thisCritMech = null;
      }

      public static void RemoveFullStructureLocationsFromCritList ( Dictionary<int, float> __result ) { try {
         if ( thisCritMech == null ) return;
         HashSet<int> removeList = new HashSet<int>();
         __result.Remove( (int) ArmorLocation.None );
         __result.Remove( (int) ArmorLocation.Invalid );
         foreach ( int armourInt in __result.Keys ) {
            if ( thisCritMech.GetCurrentArmor( (ArmorLocation) armourInt ) > 0 ) continue;
            ChassisLocations location = MechStructureRules.GetChassisLocationFromArmorLocation( (ArmorLocation) armourInt );
            float curr = thisCritMech.StructureForLocation( (int) location ), max = thisCritMech.MaxStructureForLocation( (int) location );
            if ( curr == max ) removeList.Add( armourInt );
         }
         foreach ( ChassisLocations location in removeList ) {
            Verbo( "Prevented {0} crit on {1} because it is not structurally damaged.", location, thisCritMech.DisplayName );
            __result.Remove( (int) location );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ CritFollowDamageTransfer ============

      private static int[] thisHitLocations;
      private static int thisHitIndex;

      public static void RecordHitInfo ( WeaponHitInfo hitInfo, int hitIndex, DamageType damageType ) {
         if ( damageType == DamageType.DFASelf ) return;
         thisHitLocations = hitInfo.hitLocations;
         thisHitIndex = hitIndex;
      }

      public static void ClearHitInfo () {
         thisHitLocations = null;
      }

      // Update hit location so that it will be consolidated by ConsolidateCriticalHitInfo
      public static void UpdateCritLocation ( ArmorLocation aLoc ) {
         if ( thisHitLocations == null ) return;
         if ( thisHitIndex < 0 || thisHitIndex >= thisHitLocations.Length ) return;
         thisHitLocations[ thisHitIndex ] = (int) aLoc;
      }

   }
}