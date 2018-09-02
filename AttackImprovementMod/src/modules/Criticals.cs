﻿using BattleTech;
using Harmony;
using Localize;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace Sheepy.BattleTechMod.AttackImprovementMod {
   using static Mod;
   using static System.Reflection.BindingFlags;

   public class Criticals : BattleModModule {

      private const bool DebugLog = false;
      private const ArmorLocation FrontArmours = ArmorLocation.Head | ArmorLocation.LeftArm | ArmorLocation.LeftTorso | ArmorLocation.CenterTorso
                                               | ArmorLocation.RightTorso | ArmorLocation.RightArm | ArmorLocation.LeftLeg | ArmorLocation.RightLeg;

      private static Type MechType = typeof( Mech );

      private static bool ThroughArmorCritEnabled;
      private static float MultiplierEnemy, MultiplierAlly,
         TAC_Threshold, TAC_ThresholdPerc, TAC_BaseChance, TAC_VarChance, CritChanceMin, CritChanceMax, CritChanceBase, CritChanceVar;

#pragma warning disable CS0162 // Disable "unreachable code" warnings due to DebugLog flag
      public override void CombatStartsOnce () {
         Type[] ResolveParams = new Type[]{ typeof( WeaponHitInfo ), typeof( Weapon ), typeof( MeleeAttackType ) };
         MethodInfo ResolveWeaponDamage = MechType.GetMethod( "ResolveWeaponDamage", ResolveParams );
         InitCritChance();

         if ( Settings.SkipCritingDeadMech )
            Patch( ResolveWeaponDamage, "Skip_BeatingDeadMech", null );

         if ( MultiplierEnemy != 0.2 || MultiplierAlly != 0.2 )
            Patch( typeof( CritChanceRules ), "GetCritMultiplier", "SetNPCCritMultiplier", null );

         if ( Settings.CritChanceVsTurret > 0 )
            Patch( typeof( Turret ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );
         if ( Settings.CriChanceVsVehicle > 0 )
            Patch( typeof( Vehicle ), "ResolveWeaponDamage", typeof( WeaponHitInfo ), null, "EnableNonMechCrit" );

         if ( ThroughArmorCritEnabled = Settings.CritChanceZeroArmor > 0 ) {
            Patch( ResolveWeaponDamage, "AddThroughArmorCritical", null );
            Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", "Override_ConsolidateCriticalHitInfo", null );
            InitThroughArmourCrit();

         } else {
            if ( Settings.FixFullStructureCrit ) {
               Patch( ResolveWeaponDamage, "RecordCritMech", "ClearCritMech" );
               Patch( typeof( WeaponHitInfo ), "ConsolidateCriticalHitInfo", null, "RemoveFullStructureLocationsFromCritList" );
            }
            // The settings below are built-in to generic crit system and only need to be patched when it is not used for mech.
            if ( Settings.CritIgnoreDestroyedComponent || Settings.CritIgnoreEmptySlots || Settings.CritLocationTransfer || Settings.MultupleCrit ) {
               Patch( MechType, "CheckForCrit", NonPublic, "Override_CheckForCrit", null );
               if ( Settings.CritLocationTransfer && ! Settings.CritFollowDamageTransfer ) 
                  Warn( "Disabling CritFollowDamageTransfer will cause less crit to be checked, diminishing CritLocationTransfer." );
            }
            if ( CritChanceBase != 0 || CritChanceVar != 1 )
               Patch( typeof( CritChanceRules ), "GetBaseCritChance", new Type[]{ MechType, typeof( ChassisLocations ), typeof( bool ) }, "Override_BaseCritChance", null );
            if ( CritChanceMax < 1 )
               Patch( typeof( CritChanceRules ), "GetBaseCritChance", new Type[]{ MechType, typeof( ChassisLocations ), typeof( bool ) }, null, "CapBaseCritChance" );
         }

         if ( Settings.CritFollowDamageTransfer ) {
            Patch( MechType, "TakeWeaponDamage", "RecordHitInfo", "ClearHitInfo" );
            Patch( MechType, "DamageLocation", NonPublic, "UpdateCritLocation", null );
         }

         if ( Settings.CritChanceVsTurret > 0 || Settings.CriChanceVsVehicle > 0 || ThroughArmorCritEnabled ) {
            if ( BattleMod.FoundMod( "MechEngineer.Control" ) ) { try {
               Assembly MechEngineer = AppDomain.CurrentDomain.GetAssemblies().First( e => e.GetName().Name == "MechEngineer" );
               Type MechCheckForCritPatch = MechEngineer?.GetType( "MechEngineer.MechCheckForCritPatch" );
               MechEngineerCheckCritPublishMessage = MechCheckForCritPatch?.GetMethod( "PublishMessage", Static | Public );
               MechEngineerCheckCritPostfix = MechCheckForCritPatch?.GetMethod( "Postfix", Static | Public );
               MechEngineerGetCompLocation = MechEngineer?.GetType( "MechEngineer.DamageIgnoreHelper" )?.GetMethod( "OverrideLocation" );
               MechSetCombat = typeof( Mech ).GetMethod( "set_Combat", NonPublic | Instance );
               if ( MechEngineerCheckCritPublishMessage == null || MechEngineerCheckCritPostfix == null || MechEngineerGetCompLocation == null || MechSetCombat == null ) {
                  MechEngineerCheckCritPublishMessage = MechEngineerCheckCritPostfix = MechEngineerGetCompLocation = MechSetCombat = null;
                  throw new NullReferenceException();
               }
               Info( "Attack Improvement Mod has bridged with MechEngineer.MechCheckForCritPatch on crit handling." );
            } catch ( Exception ex ) {
               Error( ex );
               BattleMod.BTML_LOG.Warn( "Attack Improvement Mod cannot bridge with MechEngineer. Component crit may not be handled properly." );
            } }
         }
      }

      public override void CombatStarts () {
         CombatResolutionConstantsDef con = CombatConstants.ResolutionConstants;
         if ( CritChanceMin != con.MinCritChance ) {
            con.MinCritChance = CritChanceMin;
            typeof( CombatGameConstants ).GetProperty( "ResolutionConstants" ).SetValue( CombatConstants, con, null );
         }
      }

      private void InitCritChance () {
         MultiplierEnemy = (float) Settings.CritChanceEnemy;
         MultiplierAlly = (float) Settings.CritChanceAlly;
         CritChanceMin = (float) Settings.CritChanceMin;
         CritChanceMax = (float) Settings.CritChanceMax;
         CritChanceBase = (float) Settings.CritChanceZeroStructure;
         CritChanceVar = (float) Settings.CritChanceFullStructure - CritChanceBase;
      }

      private void InitThroughArmourCrit () {
         if ( Settings.FixFullStructureCrit ) {
            Warn( "FullStructureCrit disabled because ThroughArmorCritical is enabled, meaning full structure can be crit'ed." );
            Settings.FixFullStructureCrit = false;
         }
         TAC_BaseChance = (float) Settings.CritChanceFullArmor;
         TAC_VarChance = (float) Settings.CritChanceZeroArmor - TAC_BaseChance;
         if ( Settings.ThroughArmorCritThreshold > 1 )
            TAC_Threshold = (float) Settings.ThroughArmorCritThreshold;
         else
            TAC_ThresholdPerc = (float)Settings.ThroughArmorCritThreshold;
         if ( Settings.ThroughArmorCritThreshold != 0 && ! Settings.CritFollowDamageTransfer )
            Warn( "Disabling CritFollowDamageTransfer may affect ThroughArmorCritThreshold calculation." );
      }

      [ HarmonyPriority( Priority.High ) ]
      public static bool Skip_BeatingDeadMech ( Mech __instance ) {
         if ( __instance.IsFlaggedForDeath || __instance.IsDead ) return false;
         return true;
      }

      // ============ Generic Critical System Support ============

      private static MethodInfo MechEngineerCheckCritPublishMessage, MechEngineerCheckCritPostfix, MechEngineerGetCompLocation, MechSetCombat;

      public static void PublishMessage ( ICombatant unit, string message, object arg, FloatieMessage.MessageNature type ) {
         MessageCenterMessage msg = new AddSequenceToStackMessage( new ShowActorInfoSequence( unit, new Text( message, new object[] { arg } ), type, true ) );
         if ( MechEngineerCheckCritPublishMessage != null )
            MechEngineerCheckCritPublishMessage.Invoke( null, new object[]{ unit.Combat.MessageCenter, msg } );
         else
            unit.Combat.MessageCenter.PublishMessage( msg );
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

      public static void CheckForAllCrits ( AIMCritInfo info ) { try {
         if ( DebugLog ) Verbo( "Start crit check on {0} by {1}", info.target, info.weapon );
         ConsolidateCrit( info );
         foreach ( var damagedLocation in damaged )
            CheckForCrit( info, damagedLocation.Key );
      }                 catch ( Exception ex ) { Error( ex ); } }

      private static void ConsolidateCrit ( AIMCritInfo info ) { try {
         damaged.Clear();
         allowConsolidateOnce = true;
         damages = info.hitInfo.ConsolidateCriticalHitInfo( GetWeaponDamage( info ) );
         damages.Remove( 0 );
         damages.Remove( 65536 );
         if ( DebugLog ) Verbo( "SplitCriticalHitInfo found {0} hit locations by {1} on {2}.", damages.Count, info.weapon, info.target );
         foreach ( var damage in damages ) {
            info.SetHitLocation( damage.Key );
            if ( ! info.CanBeCrit() ) continue;
            if ( info.IsArmourBreached ) {
               if ( DebugLog ) Verbo( "Struct damage {0} = {1}", damage.Key, damage.Value );
               damaged.Add( damage.Key, damage.Value );
               continue;
            }
            if ( ! ThroughArmorCritEnabled ) continue;
            if ( DebugLog ) Verbo( "Armour damage {0} = {1}", damage.Key, damage.Value );
            if ( ( TAC_Threshold == 0 && TAC_ThresholdPerc == 0 ) // No threshold
            /*const*/ || ( TAC_Threshold > 0 && damage.Value >= TAC_Threshold )
            /*abs% */ || ( TAC_ThresholdPerc > 0 && damage.Value >= TAC_ThresholdPerc * info.maxArmour )
            /*curr%*/ || ( TAC_ThresholdPerc < 0 && damage.Value >= TAC_ThresholdPerc * ( info.currentArmour + damage.Value ) ) )
               damaged.Add( damage.Key, damage.Value );
            else if ( DebugLog ) Verbo( "Damage not reach threshold {0} / {1}% (Armour {2}/{3})", TAC_Threshold, TAC_ThresholdPerc*100, info.currentArmour, info.maxArmour );
         }
         damages.Clear();
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void CheckForCrit ( AIMCritInfo critInfo, int hitLocation ) { try {
         if ( critInfo?.weapon == null ) return;
         critInfo.SetHitLocation( hitLocation );
         AbstractActor target = critInfo.target;
         if ( Settings.SkipCritingDeadMech && ( target.IsDead || target.IsFlaggedForDeath ) ) return;
         bool critLogged = false;
         float critChance = critInfo.GetCritChance();
         while ( critChance > 0 ) {
            float[] randomFromCache = Combat.AttackDirector.GetRandomFromCache( critInfo.hitInfo, 2 );
            if ( DebugLog ) Verbo( "Crit roll {0} < chance {1}? {2}", randomFromCache[0], critChance, randomFromCache[ 0 ] <= critChance );
            if ( randomFromCache[ 0 ] > critChance ) break;
            FindAndCritComponent( critInfo, randomFromCache[ 1 ] );
            if ( ! critLogged ) {
               AttackLog.LogCritResult( target, critInfo.weapon );
               critLogged = true;
            }
            if ( ! Settings.MultupleCrit ) break;
            critChance -= randomFromCache[ 0 ];
            if ( DebugLog ) Verbo( "Chance of next crit = {0}.", critChance );
         }
         if ( ! critLogged ) AttackLog.LogCritResult( target, critInfo.weapon );
         if ( MechEngineerCheckCritPostfix != null ) {
            Mech mech = new Mech();
            MechSetCombat.Invoke( mech, new object[]{ Combat } );
            MechEngineerCheckCritPostfix.Invoke( null, new object[]{ mech } );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      public static void FindAndCritComponent ( AIMCritInfo critInfo, float random ) {
         MechComponent component = critInfo.FindComponentFromRoll( random );
         if ( component != null ) {
            if ( DebugLog ) Verbo( "Play crit SFX and VFX on {0} ({1}) at {2}", component, component.DamageLevel, component.Location );
            PlaySFX( critInfo );
            PlayVFX( critInfo );
            AttackDirector.AttackSequence attackSequence = Combat.AttackDirector.GetAttackSequence( critInfo.hitInfo.attackSequenceId );
            if ( attackSequence != null )
               attackSequence.FlagAttackScoredCrit( component as Weapon, component as AmmunitionBox );
            ComponentDamageLevel newDamageLevel = GetDegradedComponentLevel( critInfo );
            if ( DebugLog ) Verbo( "Component damaged to {0}", newDamageLevel );
            component.DamageComponent( critInfo.hitInfo, newDamageLevel, true );
         }
      }

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

      public static void PlaySFX ( AIMCritInfo info ) {
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

      public static void PlayVFX ( AIMCritInfo info ) {
         ICombatant target = info.target;
         if ( target.GameRep == null ) return;
         MechComponent component = info.component;
         bool isAmmo = component is AmmunitionBox, isJumpJet = component is Jumpjet, isHeatSink = component.componentDef is HeatSinkDef;
         if ( target.team.LocalPlayerControlsTeam )
            AudioEventManager.PlayAudioEvent( "audioeventdef_musictriggers_combat", "critical_hit_friendly ", null, null );
         else if ( !target.team.IsFriendly( Combat.LocalPlayerTeam ) )
            AudioEventManager.PlayAudioEvent( "audioeventdef_musictriggers_combat", "critical_hit_enemy", null, null );
         if ( target.GameRep is MechRepresentation MechRep && ! isJumpJet && ! isHeatSink && ! isAmmo && component.DamageLevel > ComponentDamageLevel.Functional )
            MechRep.PlayComponentCritVFX( info.GetCritLocation() );
         if ( isAmmo && component.DamageLevel > ComponentDamageLevel.Functional )
            target.GameRep.PlayVFX( info.GetCritLocation(), Combat.Constants.VFXNames.componentDestruction_AmmoExplosion, true, Vector3.zero, true, -1f );
      }

      public static float GetAdjustedChance ( AIMCritInfo info ) {
         if ( info.target.StatCollection.GetValue<bool>( "CriticalHitImmunity" ) ) return 0;
         float chance = GetBaseChance( info ), critMultiplier = chance > 0 ? GetMultiplier( info ) : 0;
         if ( DebugLog ) Verbo( "Crit chance on {3} = {0} x {1} = {2}", chance, critMultiplier, chance * critMultiplier, info.HitLocation );
         return chance * critMultiplier;
      }

      public static float GetBaseChance ( AIMCritInfo info ) {
         float chance = 0;
         if ( info.IsArmourBreached ) {
            chance = GetBaseChance( info.currentStructure, info.maxStructure );
            if ( DebugLog ) Verbo( "Normal base crit chance = {0}/{1} = {3}", info.currentStructure, info.maxStructure, chance );
            AttackLog.LogAIMBaseCritChance( chance, info.maxStructure );
            return Mathf.Max( CritChanceMin, Mathf.Min( chance, CritChanceMax ) );
         } else if ( ThroughArmorCritEnabled ) {
            chance = GetTACBaseChance( info.currentArmour, info.maxArmour );
            if ( DebugLog ) Verbo( "TAC base crit chance = {0}/{1} = {2}", info.currentArmour, info.maxArmour, chance );
            AttackLog.LogAIMBaseCritChance( chance, info.maxArmour );
         }
         return chance;
      }

      public static float GetMultiplier ( AIMCritInfo info ) {
         float critMultiplier = Combat.CritChance.GetCritMultiplier( info.target, info.weapon, true );
         if ( DebugLog ) Verbo( "Base crit multiplier x{0}, vehicle x{1}, Turret x{2}", critMultiplier, Settings.CriChanceVsVehicle, Settings.CritChanceVsTurret );
         if ( info.target is Vehicle && Settings.CriChanceVsVehicle != 1 )
            critMultiplier *= (float) Settings.CriChanceVsVehicle;
         else if ( info.target is Turret && Settings.CritChanceVsTurret != 1 )
            critMultiplier *= (float) Settings.CritChanceVsTurret;
         AttackLog.LogCritMultiplier( critMultiplier );
         return critMultiplier;
      }

      public static MechComponent GetComponentFromRoll ( AbstractActor me, int location, float random, int MinSlots = 0 ) {
         List<MechComponent> list = new List<MechComponent>( MinSlots );
         foreach ( MechComponent component in me.allComponents ) {
            int componentLocation = MechEngineerGetCompLocation != null
                                  ? (int) MechEngineerGetCompLocation.Invoke( null, new object[]{ component } )
                                  : component.Location;
            if ( DebugLog ) Verbo( "List components at {0}, {1} location {2}, Flag = {3}", location, component, componentLocation, componentLocation & location );
            if ( ( componentLocation & location ) <= 0 ) continue;
            if ( Settings.CritIgnoreDestroyedComponent && component.DamageLevel >= ComponentDamageLevel.Destroyed ) continue;
            for ( int i = component.inventorySize ; i > 0 ; i-- )
               list.Add( component );
         }
         if ( ! Settings.CritIgnoreEmptySlots )
            for ( int i = list.Count ; i < MinSlots ; i++ )
               list.Add( null );
         int slot = (int)( list.Count * random );
         MechComponent result = slot < list.Count ? list[ slot ] : null;
         if ( list.Count <= 0 && Settings.CritLocationTransfer && me is Mech mech ) {
            ArmorLocation newLocation = MechStructureRules.GetPassthroughLocation( MechStructureRules.GetArmorFromChassisLocation( (ChassisLocations) location ) & FrontArmours, AttackDirection.FromFront );
            if ( newLocation == ArmorLocation.None &&  location == (int) ChassisLocations.Head ) 
               newLocation = ArmorLocation.CenterTorso; // Special crit passthrough rule. I say so.
            if ( DebugLog ) Verbo( "Crit list empty at {0}, transferring crit to {1}", location, newLocation );
            if ( newLocation != ArmorLocation.None ) {
               ChassisLocations chassis = MechStructureRules.GetChassisLocationFromArmorLocation( newLocation );
               result = GetComponentFromRoll( me, (int) chassis, random, mech.MechDef.GetChassisLocationDef( chassis ).InventorySlots );
            }
         } else if ( DebugLog ) Verbo( "Slot roll {0}, slot count {1}, slot {2}, component {3} status {4}", random, list.Count, slot, result, result.DamageLevel );
         AttackLog.LogCritComp( result, slot );
         return result;
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
         public bool IsArmourBreached { get => currentArmour <= 0 && ( ! Settings.FixFullStructureCrit || currentStructure < maxStructure ); }
         public abstract bool CanBeCrit ();
         public virtual  void SetHitLocation ( int location ) { HitLocation = location; }
         protected void SetStates ( float currA, float maxA, float currS, float maxS ) {
            if ( DebugLog ) Verbo( "CritInfo Location {0}: Armour {1}/{2}, Structure {3}/{4}", HitLocation, currA, maxA, currS, maxS );
            currentArmour = currA;   maxArmour = maxA;   currentStructure = currS;   maxStructure = maxS;
         }

         public abstract float GetCritChance ();
         public virtual MechComponent FindComponentFromRoll ( float random ) {
            return component = GetComponentFromRoll( target, -1, random, target.allComponents.Count );
         }
         public virtual int GetCritLocation () { return HitLocation; } // Used to play VFX
      }

      public class AIMMechCritInfo : AIMCritInfo {
         public Mech Me { get => target as Mech; }
         public ArmorLocation HitArmour { get => (ArmorLocation) HitLocation; }
         public ChassisLocations critLocation;
         public AIMMechCritInfo ( Mech target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool CanBeCrit () {
            if ( HitArmour == ArmorLocation.None || HitArmour == ArmorLocation.Invalid ) return false;
            if ( Me.GetStringForStructureLocation( critLocation ) == null ) Error( "Invalid crit location {0} from armour {1}", critLocation, HitArmour );
            return ! Me.IsLocationDestroyed( critLocation );
         }

         public override void SetHitLocation ( int location ) {
            base.SetHitLocation( location );
            critLocation = MechStructureRules.GetChassisLocationFromArmorLocation( HitArmour );
            if ( ! CanBeCrit() ) return;
            SetStates( Me.GetCurrentArmor( HitArmour ), Me.GetMaxArmor( HitArmour ), Me.GetCurrentStructure( critLocation ), Me.GetMaxStructure( critLocation ) );
         }

         public override float GetCritChance () {
            return AttackLog.LogAIMCritChance( GetAdjustedChance( this ), HitArmour );
         }

         public override MechComponent FindComponentFromRoll ( float random ) {
            return component = GetComponentFromRoll( target, (int) critLocation, random, Me.MechDef.GetChassisLocationDef( critLocation ).InventorySlots );
         }

         public override int GetCritLocation () { return (int) critLocation; }
      }

      // ============ Non-Mech Crit ============

      public class AIMVehicleInfo : AIMCritInfo {
         public Vehicle Me { get => target as Vehicle; }
         public VehicleChassisLocations CritChassis { get => (VehicleChassisLocations) HitLocation; }
         public AIMVehicleInfo ( Vehicle target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool CanBeCrit () {
            return CritChassis != VehicleChassisLocations.None && CritChassis != VehicleChassisLocations.Invalid;
         }

         public override void SetHitLocation( int location ) {
            base.SetHitLocation( location );
            SetStates( Me.GetCurrentArmor( CritChassis ), Me.GetMaxArmor( CritChassis ), Me.GetCurrentStructure( CritChassis ), Me.GetMaxStructure( CritChassis ) );
         }

         public override float GetCritChance () {
            return AttackLog.LogAIMCritChance( GetAdjustedChance( this ), CritChassis );
         }
      }

      public class AIMTurretInfo : AIMCritInfo {
         public Turret Me { get => target as Turret; }
         public BuildingLocation CritLocation { get => (BuildingLocation) HitLocation; }
         public AIMTurretInfo ( Turret target, WeaponHitInfo hitInfo, Weapon weapon ) : base( target, hitInfo, weapon ) {}

         public override bool CanBeCrit () {
            return CritLocation != BuildingLocation.None && CritLocation != BuildingLocation.Invalid;
         }

         public override void SetHitLocation ( int location ) {
            base.SetHitLocation( location );
            SetStates( Me.GetCurrentArmor( CritLocation ), Me.GetMaxArmor( CritLocation ), Me.GetCurrentStructure( CritLocation ), Me.GetMaxStructure( CritLocation ) );
         }

         public override float GetCritChance () {
            return AttackLog.LogAIMCritChance( GetAdjustedChance( this ), CritLocation );
         }
      }

      public static void EnableNonMechCrit ( AbstractActor __instance, WeaponHitInfo hitInfo ) { try {
         AttackDirector.AttackSequence attackSequence = Combat.AttackDirector.GetAttackSequence( hitInfo.attackSequenceId );
         Weapon weapon = attackSequence.GetWeapon( hitInfo.attackGroupIndex, hitInfo.attackWeaponIndex );
         //MeleeAttackType meleeAttackType = attackSequence.meleeAttackType;
         if      ( __instance is Vehicle vehicle ) CheckForAllCrits( new AIMVehicleInfo( vehicle, hitInfo, weapon ) );
         else if ( __instance is Turret  turret  ) CheckForAllCrits( new AIMTurretInfo ( turret , hitInfo, weapon ) );
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ ThroughArmorCritical ============

      private static bool allowConsolidateOnce = true;

      public static void AddThroughArmorCritical ( Mech __instance, WeaponHitInfo hitInfo, Weapon weapon, MeleeAttackType meleeAttackType ) {
         CheckForAllCrits( new AIMMechCritInfo( __instance, hitInfo, weapon ) );
      }

      // We already did all the crit in AddThroughArmorCritical, so the vanilla don't have to.
      public static bool Override_ConsolidateCriticalHitInfo ( ref Dictionary<int, float> __result ) {
         if ( allowConsolidateOnce ) {
            allowConsolidateOnce = false;
            return true;
         }
         if ( __result == null ) __result = new Dictionary<int, float>();
         else __result.Clear();
         return false;
      }

      public static float GetTACBaseChance ( float currentArmour, float maxArmour ) {
         if ( ! ThroughArmorCritEnabled ) return 0;
         float result = TAC_BaseChance;
         if ( TAC_VarChance > 0 )
            result += ( 1f - currentArmour / maxArmour ) * TAC_VarChance;
         return result;
      }

      public static float GetBaseChance ( float currentStructure, float maxStructure ) {
         float result = CritChanceBase;
         if ( CritChanceVar > 0 )
            result += ( 1f - currentStructure / maxStructure ) * CritChanceVar;
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
            Verbo( "Prevented {0} crit on {1} because it is not structurally damaged.", location, thisCritMech );
            __result.Remove( (int) location );
         }
      }                 catch ( Exception ex ) { Error( ex ); } }

      // ============ Normal Crit Chances and Multipliers ============

      private static void SetAICritMultiplier ( float setTo ) {
         CombatResolutionConstantsDef con = CombatConstants.ResolutionConstants;
         if ( setTo != con.AICritChanceBaseMultiplier ) {
            if ( DebugLog ) Verbo( "Set AICritChanceBaseMultiplie to {0}", setTo );
            con.AICritChanceBaseMultiplier = setTo;
            typeof( CombatGameConstants ).GetProperty( "ResolutionConstants" ).SetValue( CombatConstants, con, null );
         }
      }

      public static void SetNPCCritMultiplier ( Weapon weapon ) {
         Team team = weapon?.parent?.team;
         if ( team == null || team.PlayerControlsTeam ) return;
         SetAICritMultiplier( team.IsFriendly( Combat.LocalPlayerTeam ) ? MultiplierAlly : MultiplierEnemy );
      }

      public static bool Override_BaseCritChance ( ref float __result, Mech target, ChassisLocations hitLocation ) {
         if ( DebugLog ) Verbo( "Override_BaseCritChance called on {0} at {1}", target, hitLocation );
         __result = GetBaseChance( target.GetCurrentStructure( hitLocation ), target.GetMaxStructure( hitLocation ) );
         return false;
      }

      [ HarmonyPriority( Priority.VeryLow / 2 ) ] // After attack log's LogBaseCritChance
      public static void CapBaseCritChance ( ref float __result ) {
         if ( __result > CritChanceMax )
            __result = CritChanceMax;
      }

      public static bool Override_CheckForCrit ( Mech __instance, WeaponHitInfo hitInfo, ChassisLocations location, Weapon weapon ) { try {
         if ( location == ChassisLocations.None ) return false;
         if ( ThroughArmorCritEnabled ) Error( "Assertion error: Override_CheckForCrit is not designed to work with TAC." );
         ArmorLocation HitLocation = MechStructureRules.GetArmorFromChassisLocation( location ) & FrontArmours;
         if ( DebugLog ) Verbo( "Override_CheckForCrit on {0} at {1} by {2}, location placeholder = {3}", __instance, location, weapon, HitLocation );
         CheckForCrit( new AIMMechCritInfo( __instance, hitInfo, weapon ), (int) HitLocation );
         return false;
      }                 catch ( Exception ex ) { return Error( ex ); } }

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
#pragma warning restore CS0162 // Restore "unreachable code" warnings
   }
}