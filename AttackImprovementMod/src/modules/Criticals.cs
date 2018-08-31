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

      private static bool ThroughArmorCritEnabled;
      private static float ThroughArmorCritThreshold, ThroughArmorCritThresholdPerc, ThroughArmorBaseCritChance, ThroughArmorVarCritChance;

      public override void CombatStartsOnce () {
         Type[] ResolveParams = new Type[]{ typeof( WeaponHitInfo ), typeof( Weapon ), typeof( MeleeAttackType ) };
         MethodInfo ResolveWeaponDamage = MechType.GetMethod( "ResolveWeaponDamage", ResolveParams );

         if ( Settings.SkipCritingDeadMech )
            Patch( ResolveWeaponDamage, "Skip_BeatingDeadMech", null );

         if ( Settings.TurretCritMultiplier > 0 )
            Patch( typeof( Turret ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );
         if ( Settings.VehicleCritMultiplier > 0 )
            Patch( typeof( Vehicle ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );

         if ( ThroughArmorCritEnabled = Settings.ThroughArmorCritChanceZeroArmor > 0 ) {
            Patch( ResolveWeaponDamage, "AddThroughArmorCritical", null );
            Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", "Override_ConsolidateCriticalHitInfo", null );
            InitThroughArmourCrit();

         } else if ( Settings.FixFullStructureCrit ) {
            Patch( ResolveWeaponDamage, "RecordCritMech", "ClearCritMech" );
            Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", null, "RemoveFullStructureLocationsFromCritList" );
         }

         if ( Settings.CritFollowDamageTransfer ) {
            Patch( MechType, "TakeWeaponDamage", "RecordHitInfo", "ClearHitInfo" );
            Patch( MechType, "DamageLocation", NonPublic, "UpdateCritLocation", null );
         }
      }

      private void InitThroughArmourCrit () {
         if ( Settings.FixFullStructureCrit ) {
            Warn( "FullStructureCrit disabled because ThroughArmorCritical is enabled, meaning full structure can be crit'ed." );
            Settings.FixFullStructureCrit = false;
         }
         ThroughArmorBaseCritChance = (float) Settings.ThroughArmorCritChanceFullArmor;
         ThroughArmorVarCritChance = (float) Settings.ThroughArmorCritChanceZeroArmor - ThroughArmorBaseCritChance;
         if ( Settings.ThroughArmorCritThreshold > 1 )
            ThroughArmorCritThreshold = (float) Settings.ThroughArmorCritThreshold;
         else
            ThroughArmorCritThresholdPerc = (float)Settings.ThroughArmorCritThreshold;
         if ( Settings.ThroughArmorCritThreshold != 0 && ! Settings.CritFollowDamageTransfer )
            Warn( "Disabling CritFollowDamageTransfer may affect ThroughArmorCritThreshold calculation." );
      }

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

      // ============ Generic Critical System Core ============

      private static Dictionary<int, float> damages = new Dictionary<int, float>(), damaged = new Dictionary<int, float>();

      private static void ConsolidateAIMCrit ( AIMCritInfo info ) { try {
         damages = info.hitInfo.ConsolidateCriticalHitInfo( GetWeaponDamage( info ) );
         damaged.Clear();
         //Verbo( "SplitCriticalHitInfo found {0} hit locations by {1} on {2}.", damages.Count, info.weapon, info.target );
         foreach ( var damage in damages ) {
            info.SetLocation( damage.Key );
            if ( ! info.IsValidLocation() ) continue;
            if ( info.IsArmourBreached ) {
               //Verbo( "Struct damage {0} = {1}", damage.Key, damage.Value );
               damaged.Add( info.HitLocation, damage.Value );
               continue;
            }
            if ( ! ThroughArmorCritEnabled ) continue;
            //Verbo( "Armour damage {0} = {1}", damage.Key, damage.Value );
            if ( ( ThroughArmorCritThreshold == 0 && ThroughArmorCritThresholdPerc == 0 ) // No threshold
            /*const*/ || ( ThroughArmorCritThreshold > 0 && damage.Value > ThroughArmorCritThreshold )
            /*abs% */ || ( ThroughArmorCritThresholdPerc > 0 && damage.Value > ThroughArmorCritThresholdPerc * info.maxArmour )
            /*curr%*/ || ( ThroughArmorCritThresholdPerc < 0 && damage.Value > ThroughArmorCritThresholdPerc * ( info.currentArmour + damage.Value ) ) )
               damaged.Add( info.HitLocation, damage.Value );
            //else Verbo( "Damage not reach threshold {0} / {1}% (Armour {2}/{3})", ThroughArmorCritThreshold, ThroughArmorCritThresholdPerc*100, armour.x, armour.y );
         }
         damages.Clear();
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void CheckForCrit ( AIMCritInfo critInfo, int hitLocation ) { try {
         if ( critInfo?.weapon == null ) return;
         critInfo.SetLocation( hitLocation );
         AbstractActor target = critInfo.target;
         if ( Settings.SkipCritingDeadMech && ( target.IsDead || target.IsFlaggedForDeath ) ) return;
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
         AttackLog.LogCritResult( target, critInfo.weapon );
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
            MechRep.PlayComponentCritVFX( info.GetCritLocation() );
         if ( AmmoCrited != null && component.DamageLevel > ComponentDamageLevel.Functional )
            target.GameRep.PlayVFX( info.GetCritLocation(), Combat.Constants.VFXNames.componentDestruction_AmmoExplosion, true, Vector3.zero, true, -1f );
      }

      // ============ AIMCritInfo ============

      public abstract class AIMCritInfo {
         public AbstractActor target;
         public WeaponHitInfo hitInfo;
         public Weapon weapon;
         public MechComponent component;

         public AIMCritInfo( AbstractActor target, WeaponHitInfo hitInfo, Weapon weapon ) {
            this.target = target;
            this.hitInfo = hitInfo;
            this.weapon = weapon;
         }

         public int HitLocation { get; protected set; }
         public float currentArmour, maxArmour, currentStructure, maxStructure;
         public bool IsArmourBreached { get => maxArmour <= 0 && ( ! Settings.FixFullStructureCrit || currentStructure < maxStructure ); }
         public abstract bool IsValidLocation();
         public virtual  void SetLocation ( int location ) { HitLocation = location; }
         protected void SetStates ( float currA, float maxA, float currS, float maxS ) {
            currentArmour = currA;   maxArmour = maxA;   currentStructure = currS;   maxStructure = maxS;
         }

         public abstract float GetCritChance ();
         public virtual  MechComponent FindComponentInSlot ( float random ) { // TODO: take component slot into account
            float slotCount = target.allComponents.Count;
            int slot = (int)(slotCount * random );
            component = target.allComponents[ slot ];
            AttackLog.LogCritComp( component, slot );
            return component;
         }
         public virtual int GetCritLocation() { return HitLocation; } // Used to play VFX
      }

      public class AIMMechCritInfo : AIMCritInfo {
         public Mech Me { get => target as Mech; }
         public ArmorLocation HitArmour { get => (ArmorLocation) HitLocation; }
         public ChassisLocations critLocation;
         public AIMMechCritInfo ( Mech target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool IsValidLocation () {
            if ( HitArmour == ArmorLocation.None || HitArmour == ArmorLocation.Invalid ) return false;
            return ! Me.IsLocationDestroyed( critLocation );
         }

         public override void SetLocation ( int location ) {
            base.SetLocation( location );
            critLocation = MechStructureRules.GetChassisLocationFromArmorLocation( HitArmour );
            if ( ! IsValidLocation() ) return;
            SetStates( Me.GetCurrentArmor( HitArmour ), Me.GetMaxArmor( HitArmour ), Me.GetCurrentStructure( critLocation ), Me.GetMaxStructure( critLocation ) );
         }

         public override float GetCritChance() {
            if ( ! IsArmourBreached )
               return AttackLog.LogAIMCritChance( GetAIMCritChance( this ), critLocation );
            else
               return Combat.CritChance.GetCritChance( Me, critLocation, weapon, true );
         }

         public override MechComponent FindComponentInSlot( float random ) {
            float slotCount = Me.MechDef.GetChassisLocationDef( critLocation ).InventorySlots;
            int slot = (int)(slotCount * random );
            return component = Me.GetComponentInSlot( critLocation, slot );
         }

         public override int GetCritLocation() { return (int) critLocation; }
      }

      public class AIMVehicleInfo : AIMCritInfo {
         public Vehicle Me { get => target as Vehicle; }
         public VehicleChassisLocations CritChassis { get => (VehicleChassisLocations) HitLocation; }
         public AIMVehicleInfo ( Vehicle target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool IsValidLocation () {
            return CritChassis != VehicleChassisLocations.None && CritChassis != VehicleChassisLocations.Invalid;
         }

         public override void SetLocation( int location ) {
            base.SetLocation( location );
            SetStates( Me.GetCurrentArmor( CritChassis ), Me.GetMaxArmor( CritChassis ), Me.GetCurrentStructure( CritChassis ), Me.GetMaxStructure( CritChassis ) );
         }

         public override float GetCritChance () {
            return AttackLog.LogAIMCritChance( GetAIMCritChance( this ), CritChassis );
         }
      }

      public class AIMTurretInfo : AIMCritInfo {
         public Turret Me { get => target as Turret; }
         public BuildingLocation CritLocation { get => (BuildingLocation) HitLocation; }
         public AIMTurretInfo ( Turret target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool IsValidLocation () {
            return CritLocation != BuildingLocation.None && CritLocation != BuildingLocation.Invalid;
         }

         public override void SetLocation( int location ) {
            base.SetLocation( location );
            SetStates( Me.GetCurrentArmor( CritLocation ), Me.GetMaxArmor( CritLocation ), Me.GetCurrentStructure( CritLocation ), Me.GetMaxStructure( CritLocation ) );
         }

         public override float GetCritChance () {
            return AttackLog.LogAIMCritChance( GetAIMCritChance( this ), CritLocation );
         }
      }

      // ============ Universal Crit Patch ============

      public static void EnableNonMechCrit ( AbstractActor __instance, WeaponHitInfo hitInfo ) { try {
         AttackDirector.AttackSequence attackSequence = Combat.AttackDirector.GetAttackSequence( hitInfo.attackSequenceId );
         Weapon weapon = attackSequence.GetWeapon( hitInfo.attackGroupIndex, hitInfo.attackWeaponIndex );
         //MeleeAttackType meleeAttackType = attackSequence.meleeAttackType;
         if      ( __instance is Vehicle vehicle ) ResolveAIMWeaponDamage( new AIMVehicleInfo( vehicle, hitInfo, weapon ) );
         else if ( __instance is Turret  turret  ) ResolveAIMWeaponDamage( new AIMTurretInfo ( turret , hitInfo, weapon ) );
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void ResolveAIMWeaponDamage ( AIMCritInfo info ) { try {
         ConsolidateAIMCrit( info );
         foreach ( var damagedLocation in damaged )
            CheckForCrit( info, damagedLocation.Key );
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static float GetAIMCritChance ( AIMCritInfo info ) {
         if ( info.target.StatCollection.GetValue<bool>( "CriticalHitImmunity" ) ) return 0;
         float chance = 0, critMultiplier = 0;
         if ( info.IsArmourBreached ) {
            chance = info.currentArmour / info.maxStructure;
            AttackLog.LogAIMBaseCritChance( chance, info.maxStructure );
            chance = Mathf.Max( chance, CombatConstants.ResolutionConstants.MinCritChance );
         } else
            chance = GetTACBaseChance( info.currentArmour, info.maxArmour );
         if ( chance > 0 )
            critMultiplier = Combat.CritChance.GetCritMultiplier( info.target, info.weapon, true );
         float result = chance * critMultiplier;
         return result;
      }

      // ============ ThroughArmorCritical ============

      public static void AddThroughArmorCritical ( Mech __instance, WeaponHitInfo hitInfo, Weapon weapon, MeleeAttackType meleeAttackType ) {
         ResolveAIMWeaponDamage( new AIMMechCritInfo( __instance, hitInfo, weapon ) );
      }

      // We already did all the crit in AddThroughArmorCritical, so the vanilla don't have to.
      public static bool Override_ConsolidateCriticalHitInfo ( ref Dictionary<int, float> __result ) {
         if ( __result == null ) __result = new Dictionary<int, float>();
         else __result.Clear();
         return false;
      }

      public static float GetTACBaseChance ( float currentArmour, float maxArmour ) {
         if ( ! ThroughArmorCritEnabled ) return 0;
         float result = ThroughArmorBaseCritChance;
         if ( ThroughArmorVarCritChance > 0 )
            result += ( 1f - currentArmour / maxArmour ) * ThroughArmorVarCritChance;
         AttackLog.LogAIMBaseCritChance( result, maxArmour );
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