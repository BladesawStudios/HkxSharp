namespace HkxSharp;

public static class HkClasses
{
    public const string Definitions = """
hkBaseObject virtual
hkReferencedObject : hkBaseObject
  u32 memSizeAndRefCount nosave

hkRootLevelContainerNamedVariant
  str name
  str className
  ptr variant
hkRootLevelContainer
  array<hkRootLevelContainerNamedVariant> namedVariants

hkAabb
  vec4 min
  vec4 max

hkcdShape : hkReferencedObject
  u8 type nosave
  u8 dispatchType
  u8 bitsPerKey
  u8 shapeInfoCodecType
hkpShapeBase : hkcdShape
hkpShape : hkpShapeBase
  ulong userData
hkpShapeContainer virtual
hkpSingleShapeContainer : hkpShapeContainer
  ptr<hkpShape> childShape
hkpSphereRepShape : hkpShape
hkpConvexShape : hkpSphereRepShape
  f32 radius
hkpSphereShape : hkpConvexShape
  u32[3] pad16 nosave
hkpBoxShape : hkpConvexShape
  vec4 halfExtents
hkpCapsuleShape : hkpConvexShape
  vec4 vertexA
  vec4 vertexB
hkpCylinderShape : hkpConvexShape
  f32 cylRadius
  f32 cylBaseRadiusFactorForHeightFieldCollisions
  vec4 vertexA
  vec4 vertexB
  vec4 perpendicular1
  vec4 perpendicular2
hkpConvexTransformShapeBase : hkpConvexShape
  hkpSingleShapeContainer childShape
  s32 childShapeSizeForSpu nosave
hkpConvexTranslateShape : hkpConvexTransformShapeBase
  vec4 translation
hkpConvexTransformShape : hkpConvexTransformShapeBase
  qstransform transform
  vec4 extraScale
hkFourTransposedPoints
  vec4[3] vertices
hkpConvexVerticesConnectivity : hkReferencedObject
  array<u16> vertexIndices
  array<u8> numVerticesPerFace
hkpConvexVerticesShape : hkpConvexShape
  vec4 aabbHalfExtents
  vec4 aabbCenter
  array<hkFourTransposedPoints> rotatedVertices
  s32 numVertices
  bool useSpuBuffer nosave
  array<vec4> planeEquations
  ptr<hkpConvexVerticesConnectivity> connectivity
hkpShapeCollection : hkpShape
  vptr shapeContainer
  bool disableWelding
  u8 collectionType
hkpListShapeChildInfo align16
  ptr<hkpShape> shape
  u32 collisionFilterInfo
  u16 shapeInfo
  s16 shapeSize
  s32 numChildShapes
hkpListShape : hkpShapeCollection
  array<hkpListShapeChildInfo> childInfo
  u16 flags
  u16 numDisabledChildren
  vec4 aabbHalfExtents
  vec4 aabbCenter
  u32[8] enabledChildren
hkpBvTreeShape : hkpShape
  u8 bvTreeType

hkcdStaticTreeCodec3Axis4
  u8[3] xyz
  u8 data
hkcdStaticTreeCodec3Axis5
  u8[3] xyz
  u8 hiData
  u8 loData
hkcdStaticTreeCodec3Axis6
  u8[3] xyz
  u8 hiData
  u16 loData
hkcdStaticTreeDynamicStorage4
  array<hkcdStaticTreeCodec3Axis4> nodes
hkcdStaticTreeDynamicStorage5
  array<hkcdStaticTreeCodec3Axis5> nodes
hkcdStaticTreeDynamicStorage6
  array<hkcdStaticTreeCodec3Axis6> nodes
hkcdStaticTreeTreeDynamicStorage4 : hkcdStaticTreeDynamicStorage4
  hkAabb domain
hkcdStaticTreeTreeDynamicStorage5 : hkcdStaticTreeDynamicStorage5
  hkAabb domain
hkcdStaticTreeTreeDynamicStorage6 : hkcdStaticTreeDynamicStorage6
  hkAabb domain
hkcdStaticMeshTreeBaseSectionSharedVertices
  u32 data
hkcdStaticMeshTreeBaseSectionPrimitives
  u32 data
hkcdStaticMeshTreeBaseSectionDataRuns
  u32 data
hkcdStaticMeshTreeBaseSection : hkcdStaticTreeTreeDynamicStorage4
  f32[6] codecParms
  u32 firstPackedVertex
  hkcdStaticMeshTreeBaseSectionSharedVertices sharedVertices
  hkcdStaticMeshTreeBaseSectionPrimitives primitives
  hkcdStaticMeshTreeBaseSectionDataRuns dataRuns
  u8 numPackedVertices
  u8 numSharedIndices
  u16 leafIndex
  u8 page
  u8 flags
  u8 layerData
  u8 unusedData
hkcdStaticMeshTreeBasePrimitive
  u8[4] indices
hkcdStaticMeshTreeBase : hkcdStaticTreeTreeDynamicStorage5
  s32 numPrimitiveKeys
  s32 bitsPerKey
  u32 maxKeyValue
  array<hkcdStaticMeshTreeBaseSection> sections
  array<hkcdStaticMeshTreeBasePrimitive> primitives
  array<u16> sharedVerticesIndex
hkpBvCompressedMeshShapeTreeDataRun
  u32 value
  u8 index
  u8 count
hkpBvCompressedMeshShapeTree : hkcdStaticMeshTreeBase
  array<u32> packedVertices
  array<u64> sharedVertices
  array<hkpBvCompressedMeshShapeTreeDataRun> primitiveDataRuns
hkpBvCompressedMeshShape : hkpBvTreeShape
  vptr shapeContainer
  f32 convexRadius
  u8 weldingType
  bool hasPerPrimitiveCollisionFilterInfo
  bool hasPerPrimitiveUserData
  array<u32> collisionFilterInfoPalette
  array<u32> userDataPalette
  array<str> userStringPalette
  hkpBvCompressedMeshShapeTree tree

hkpShapeKeyTableBlock
  u32[63] slots
  ptr<hkpShapeKeyTableBlock> next
hkpShapeKeyTable
  ptr<hkpShapeKeyTableBlock> lists
  u32 occupancyBitField
hkpStaticCompoundShapeInstance align16
  qstransform transform
  ptr<hkpShape> shape
  u32 filterInfo
  u32 childFilterInfoMask
  ulong userData
hkpStaticCompoundShape : hkpBvTreeShape
  vptr shapeContainer
  s8 numBitsForChildShapeKey
  s8 referencePolicy
  u32 childShapeKeyMask
  array<hkpStaticCompoundShapeInstance> instances
  array<u16> instanceExtraInfos
  hkpShapeKeyTable disabledLargeShapeKeyTable
  hkcdStaticTreeTreeDynamicStorage6 tree

hkpCdBody
  ptr<hkpShape> shape
  u32 shapeKey
  ptr motion nosave
  ptr parent nosave
hkpBroadPhaseHandle
  u32 id nosave
hkpTypedBroadPhaseHandle : hkpBroadPhaseHandle
  s8 type
  s8 ownerOffset nosave
  s8 objectQualityType
  u32 collisionFilterInfo
hkpCollidableBoundingVolumeData
  u32[3] min nosave
  u8[3] expansionMin nosave
  u8 expansionShift nosave
  u32[3] max nosave
  u8[3] expansionMax nosave
  u8 padding nosave
  u16 numChildShapeAabbs nosave
  u16 capacityChildShapeAabbs nosave
  ptr childShapeAabbs nosave
  ptr childShapeKeys nosave
hkpCollidable : hkpCdBody
  s8 ownerOffset nosave
  s8 forceCollideOntoPpu
  s16 shapeSizeOnSpu nosave
  hkpTypedBroadPhaseHandle broadPhaseHandle
  hkpCollidableBoundingVolumeData boundingVolumeData nosave
  f32 allowedPenetrationDepth
hkpLinkedCollidableCollisionEntry
  ptr agentEntry
  ptr partner
hkpLinkedCollidable : hkpCollidable
  array<hkpLinkedCollidableCollisionEntry> collisionEntries nosave
hkMultiThreadCheck
  u32 threadId nosave
  s32 stackTraceId nosave
  u16 markCount nosave
  u16 markBitStack nosave
hkSimplePropertyValue
  u64 data
hkSimpleProperty
  u32 key
  u32 alignmentPadding nosave
  hkSimplePropertyValue value
hkpWorldObject : hkReferencedObject
  ptr world nosave
  ulong userData
  hkpLinkedCollidable collidable
  hkMultiThreadCheck multiThreadCheck nosave
  str name
  array<hkSimpleProperty> properties
hkpMaterial
  s8 responseType
  half rollingFrictionMultiplier
  f32 friction
  f32 restitution
hkpEntitySpuCollisionCallback
  ptr util nosave
  u16 capacity nosave
  u8 eventFilter
  u8 userFilter
hkSweptTransform
  vec4 centerOfMass0
  vec4 centerOfMass1
  quat rotation0
  quat rotation1
  vec4 centerOfMassLocal
hkMotionState
  transform transform
  hkSweptTransform sweptTransform
  vec4 deltaAngle
  f32 objectRadius
  half linearDamping
  half angularDamping
  half timeFactor
  u8 maxLinearVelocity
  u8 maxAngularVelocity
  u8 deactivationClass
hkpMotion : hkReferencedObject
  u8 type
  u8 deactivationIntegrateCounter
  u16[2] deactivationNumInactiveFrames
  hkMotionState motionState
  vec4 inertiaAndMassInv
  vec4 linearVelocity
  vec4 angularVelocity
  vec4[2] deactivationRefPosition
  u32[2] deactivationRefOrientation
  ptr savedMotion
  u16 savedQualityTypeIndex
  half gravityFactor
hkpKeyframedRigidMotion : hkpMotion
hkpMaxSizeMotion : hkpKeyframedRigidMotion
hkpEntity : hkpWorldObject
  hkpMaterial material
  ptr limitContactImpulseUtilAndFlag nosave
  f32 damageMultiplier
  ptr breakableBody nosave
  u32 solverData nosave
  u16 storageIndex
  u16 contactPointCallbackDelay
  sarray<u8> constraintsMaster nosave
  array<ptr> constraintsSlave nosave
  array<u8> constraintRuntime nosave
  ptr simulationIsland nosave
  s8 autoRemoveLevel
  u8 numShapeKeysInContactPointProperties
  u8 responseModifierFlags
  u32 uid
  hkpEntitySpuCollisionCallback spuCollisionCallback
  hkpMaxSizeMotion motion
  sarray<ptr> contactListeners nosave
  sarray<ptr> actions nosave
  ptr<hkLocalFrame> localFrame
  ptr extendedListeners nosave
  u32 npData
hkpRigidBody : hkpEntity

hkpPhysicsSystem : hkReferencedObject
  array<ptr<hkpRigidBody>> rigidBodies
  array<ptr<hkpConstraintInstance>> constraints
  array<ptr> actions
  array<ptr> phantoms
  str name
  ulong userData
  bool active
hkpPhysicsData : hkReferencedObject
  ptr worldCinfo
  array<ptr<hkpPhysicsSystem>> systems

hkLocalFrame : hkReferencedObject

hkpConstraintData : hkReferencedObject
  ulong userData
hkpConstraintMotor : hkReferencedObject
  u8 type
hkpLimitedForceConstraintMotor : hkpConstraintMotor
  f32 minForce
  f32 maxForce
hkpPositionConstraintMotor : hkpLimitedForceConstraintMotor
  f32 tau
  f32 damping
  f32 proportionalRecoveryVelocity
  f32 constantRecoveryVelocity
hkpConstraintAtom
  u16 type
hkpSetLocalTransformsConstraintAtom : hkpConstraintAtom align16
  transform transformA
  transform transformB
hkpSetupStabilizationAtom : hkpConstraintAtom align16
  bool enabled
  u8 padding
  f32 maxLinImpulse
  f32 maxAngImpulse
  f32 maxAngle
hkpRagdollMotorConstraintAtom : hkpConstraintAtom align16
  bool isEnabled
  s16 initializedOffset
  s16 previousTargetAnglesOffset
  mat3 target_bRca
  ptr<hkpConstraintMotor>[3] motors
hkpAngMotorConstraintAtom : hkpConstraintAtom align16
  bool isEnabled
  u8 motorAxis
  s16 initializedOffset
  s16 previousTargetAngleOffset
  ptr<hkpConstraintMotor> motor
  s16 correspondingAngLimitSolverResultOffset
  f32 targetAngle
hkpAngFrictionConstraintAtom : hkpConstraintAtom align16
  u8 isEnabled
  u8 firstFrictionAxis
  u8 numFrictionAxes
  f32 maxFrictionTorque
  u8[4] padding
hkpTwistLimitConstraintAtom : hkpConstraintAtom align16
  u8 isEnabled
  u8 twistAxis
  u8 refAxis
  f32 minAngle
  f32 maxAngle
  f32 angularLimitsTauFactor
  f32 angularLimitsDampFactor
hkpConeLimitConstraintAtom : hkpConstraintAtom align16
  u8 isEnabled
  u8 twistAxisInA
  u8 refAxisInB
  u8 angleMeasurementMode
  u8 memOffsetToAngleOffset
  f32 minAngle
  f32 maxAngle
  f32 angularLimitsTauFactor
  f32 angularLimitsDampFactor
hkpAngLimitConstraintAtom : hkpConstraintAtom align16
  u8 isEnabled
  u8 limitAxis
  u8 cosineAxis
  f32 minAngle
  f32 maxAngle
  f32 angularLimitsTauFactor
  f32 angularLimitsDampFactor
hkp2dAngConstraintAtom : hkpConstraintAtom align16
  u8 freeRotationAxis
hkpBallSocketConstraintAtom : hkpConstraintAtom align16
  u8 solvingMethod
  u8 bodiesToNotify
  u8 velocityStabilizationFactor
  bool enableLinearImpulseLimit
  f32 breachImpulse
  f32 inertiaStabilizationFactor
hkpRagdollConstraintDataAtoms align16
  hkpSetLocalTransformsConstraintAtom transforms
  hkpSetupStabilizationAtom setupStabilization
  hkpRagdollMotorConstraintAtom ragdollMotors
  hkpAngFrictionConstraintAtom angFriction
  hkpTwistLimitConstraintAtom twistLimit
  hkpConeLimitConstraintAtom coneLimit
  hkpConeLimitConstraintAtom planesLimit
  hkpBallSocketConstraintAtom ballSocket
hkpRagdollConstraintData : hkpConstraintData
  hkpRagdollConstraintDataAtoms atoms
hkpLimitedHingeConstraintDataAtoms align16
  hkpSetLocalTransformsConstraintAtom transforms
  hkpSetupStabilizationAtom setupStabilization
  hkpAngMotorConstraintAtom angMotor
  hkpAngFrictionConstraintAtom angFriction
  hkpAngLimitConstraintAtom angLimit
  hkp2dAngConstraintAtom twoDAng
  hkpBallSocketConstraintAtom ballSocket
hkpLimitedHingeConstraintData : hkpConstraintData
  hkpLimitedHingeConstraintDataAtoms atoms
hkpConstraintInstance : hkReferencedObject
  ptr owner nosave
  ptr<hkpConstraintData> data
  ptr constraintModifiers
  ptr<hkpEntity>[2] entities
  u8 priority
  bool wantRuntime
  u8 destructionRemapInfo
  sarray<ptr> listeners nosave
  str name
  ulong userData
  ptr internal nosave
  u32 uid nosave

hkaBone
  str name
  bool lockTranslation
hkaSkeletonLocalFrameOnBone
  ptr<hkLocalFrame> localFrame
  s32 boneIndex
hkaSkeletonPartition
  str name
  s16 startBoneIndex
  s16 numBones
hkaSkeleton : hkReferencedObject
  str name
  array<s16> parentIndices
  array<hkaBone> bones
  array<qstransform> referencePose
  array<f32> referenceFloats
  array<str> floatSlots
  array<hkaSkeletonLocalFrameOnBone> localFrames
  array<hkaSkeletonPartition> partitions
hkaAnimationContainer : hkReferencedObject
  array<ptr<hkaSkeleton>> skeletons
  array<ptr> animations
  array<ptr> bindings
  array<ptr> attachments
  array<ptr> skins
hkaSkeletonMapperDataPartitionMappingRange
  s32 startMappingIndex
  s32 numMappings
hkaSkeletonMapperDataSimpleMapping align16
  s16 boneA
  s16 boneB
  qstransform aFromBTransform
hkaSkeletonMapperDataChainMapping align16
  s16 startBoneA
  s16 endBoneA
  s16 startBoneB
  s16 endBoneB
  qstransform startAFromBTransform
  qstransform endAFromBTransform
hkaSkeletonMapperData align16
  ptr<hkaSkeleton> skeletonA
  ptr<hkaSkeleton> skeletonB
  array<s16> partitionMap
  array<hkaSkeletonMapperDataPartitionMappingRange> simpleMappingPartitionRanges
  array<hkaSkeletonMapperDataPartitionMappingRange> chainMappingPartitionRanges
  array<hkaSkeletonMapperDataSimpleMapping> simpleMappings
  array<hkaSkeletonMapperDataChainMapping> chainMappings
  array<s16> unmappedBones
  qstransform extractedMotionMapping
  bool keepUnmappedLocal
  s32 mappingType
hkaSkeletonMapper : hkReferencedObject
  hkaSkeletonMapperData mapping
hkaRagdollInstance : hkReferencedObject
  array<ptr<hkpRigidBody>> rigidBodies
  array<ptr<hkpConstraintInstance>> constraints
  array<s32> boneToRigidBodyMap
  ptr<hkaSkeleton> skeleton

hclShape : hkReferencedObject
  s32 type
hclCapsuleShape : hclShape
  vec4 start
  vec4 end
  vec4 dir
  f32 radius
  f32 capLenSqrdInv
hclSphereShape : hclShape
  vec4 sphere
hclPlaneShape : hclShape
  vec4 planeEquation
hclCollidable : hkReferencedObject
  str name
  transform transform
  vec4 linearVelocity
  vec4 angularVelocity
  bool pinchDetectionEnabled
  s8 pinchDetectionPriority
  f32 pinchDetectionRadius
  ptr<hclShape> shape
hclBufferLayoutBufferElement
  u8 vectorConversion
  u8 vectorSize
  u8 slotId
  u8 slotStart
hclBufferLayoutSlot
  u8 flags
  u8 stride
hclBufferLayout
  hclBufferLayoutBufferElement[4] elementsLayout
  hclBufferLayoutSlot[4] slots
  u8 numSlots
  u8 triangleFormat
hclBufferDefinition : hkReferencedObject
  str name
  s32 type
  s32 subType
  u32 numVertices
  u32 numTriangles
  hclBufferLayout bufferLayout
hclScratchBufferDefinition : hclBufferDefinition
  array<u16> triangleIndices
  bool storeNormals
  bool storeTangentsAndBiTangents
hclTransformSetDefinition : hkReferencedObject
  str name
  s32 type
  u32 numTransforms
hclSimClothDataOverridableSimulationInfo align16
  vec4 gravity
  f32 globalDampingPerSecond
  f32 collisionTolerance
  u32 unk0
  u32 unk1
hclSimClothDataParticleData
  f32 mass
  f32 invMass
  f32 radius
  f32 friction
hclSimClothDataCollidableTransformMap
  s32 transformSetIndex
  array<u32> transformIndices
  array<mat4> offsets
hclSimClothDataCollidablePinchingData
  bool pinchDetectionEnabled
  s8 pinchDetectionPriority
  f32 pinchDetectionRadius
hclSimClothDataLandscapeCollisionData
  f32 landscapeRadius
  bool enableStuckParticleDetection
  f32 stuckParticlesStretchFactorSq
  bool pinchDetectionEnabled
  s8 pinchDetectionPriority
  f32 pinchDetectionRadius
hclSimClothDataTransferMotionData
  u32 transformSetIndex
  u32 transformIndex
  bool transferTranslationMotion
  f32 minTranslationSpeed
  f32 maxTranslationSpeed
  f32 minTranslationBlend
  f32 maxTranslationBlend
  bool transferRotationMotion
  f32 minRotationSpeed
  f32 maxRotationSpeed
  f32 minRotationBlend
  f32 maxRotationBlend
hclSimClothData : hkReferencedObject
  hclSimClothDataOverridableSimulationInfo simulationInfo
  str name
  array<hclSimClothDataParticleData> particleDatas
  array<u16> fixedParticles
  array<u16> triangleIndices
  array<u8> triangleFlips
  f32 totalMass
  hclSimClothDataCollidableTransformMap collidableTransformMap
  array<ptr<hclCollidable>> perInstanceCollidables
  array<ptr<hclConstraintSet>> staticConstraintSets
  array<ptr<hclConstraintSet>> antiPinchConstraintSets
  array<ptr<hclSimClothPose>> simClothPoses
  array<ptr<hclConstraintSet>> actionConstraintSets
  array<u32> staticCollisionMasks
  array<bool> perParticlePinchDetectionEnabledFlags
  array<hclSimClothDataCollidablePinchingData> collidablePinchingDatas
  u16 minPinchedParticleIndex
  u16 maxPinchedParticleIndex
  u32 maxCollisionPairs
  f32 maxParticleRadius
  hclSimClothDataLandscapeCollisionData landscapeCollisionData
  u32 numLandscapeCollidableParticles
  bool doNormals
  hclSimClothDataTransferMotionData transferMotionData
hclSimClothPose : hkReferencedObject
  str name
  array<vec4> positions
hclConstraintSet : hkReferencedObject
  str name
  u32 constraintId
hclStandardLinkConstraintSetLink
  u16 particleA
  u16 particleB
  f32 restLength
  f32 stiffness
hclStandardLinkConstraintSet : hclConstraintSet
  array<hclStandardLinkConstraintSetLink> links
hclStretchLinkConstraintSetLink
  u16 particleA
  u16 particleB
  f32 restLength
  f32 stiffness
hclStretchLinkConstraintSet : hclConstraintSet
  array<hclStretchLinkConstraintSetLink> links
hclCompressibleLinkConstraintSetLink
  u16 particleA
  u16 particleB
  f32 restLength
  f32 compressionLength
  f32 stiffness
hclCompressibleLinkConstraintSet : hclConstraintSet
  array<hclCompressibleLinkConstraintSetLink> links
hclBendLinkConstraintSetLink
  u16 particleA
  u16 particleB
  f32 bendMinLength
  f32 stretchMaxLength
  f32 bendStiffness
  f32 stretchStiffness
hclBendLinkConstraintSet : hclConstraintSet
  array<hclBendLinkConstraintSetLink> links
hclBendStiffnessConstraintSetLink
  f32 weightA
  f32 weightB
  f32 weightC
  f32 weightD
  f32 bendStiffness
  f32 restCurvature
  u16 particleA
  u16 particleB
  u16 particleC
  u16 particleD
hclBendStiffnessConstraintSet : hclConstraintSet
  array<hclBendStiffnessConstraintSetLink> links
  bool useRestPoseConfig
  bool clampBendStiffness
hclBonePlanesConstraintSetBonePlane align16
  vec4 planeEquationBone
  u16 particleIndex
  u16 transformIndex
  f32 stiffness
hclBonePlanesConstraintSet : hclConstraintSet
  array<hclBonePlanesConstraintSetBonePlane> bonePlanes
  u32 transformSetIndex
hclLocalRangeConstraintSetLocalConstraint
  u16 particleIndex
  u16 referenceVertex
  f32 maximumDistance
  f32 maxNormalDistance
  f32 minNormalDistance
hclLocalRangeConstraintSet : hclConstraintSet
  array<hclLocalRangeConstraintSetLocalConstraint> localConstraints
  u32 referenceMeshBufferIdx
  f32 stiffness
  u32 shapeType
  bool applyNormalComponent
hclTransitionConstraintSetPerParticle
  u16 particleIndex
  u16 referenceVertex
  f32 toAnimDelay
  f32 toSimDelay
  f32 toSimMaxDistance
hclTransitionConstraintSet : hclConstraintSet
  array<hclTransitionConstraintSetPerParticle> perParticleData
  f32 toAnimPeriod
  f32 toAnimPlusDelayPeriod
  f32 toSimPeriod
  f32 toSimPlusDelayPeriod
  u32 referenceMeshBufferIdx
hclVolumeConstraintFrameData align16
  vec4 frameVector
  u16 particleIndex
  f32 weight
hclVolumeConstraintApplyData align16
  vec4 frameVector
  u16 particleIndex
  f32 stiffness
hclVolumeConstraint : hclConstraintSet
  array<hclVolumeConstraintFrameData> frameDatas
  array<hclVolumeConstraintApplyData> applyDatas
hclOperator : hkReferencedObject
  str name
  u32 operatorID
hclMoveParticlesOperatorVertexParticlePair
  u16 vertexIndex
  u16 particleIndex
hclMoveParticlesOperator : hclOperator
  array<hclMoveParticlesOperatorVertexParticlePair> vertexParticlePairs
  u32 simClothIndex
  u32 refBufferIdx
hclSimulateOperator : hclOperator
  u32 simClothIndex
  u32 subSteps
  s32 numberOfSolveIterations
  array<s32> constraintExecution
  bool adaptConstraintStiffness
hclSimpleMeshBoneDeformOperatorTriangleBonePair
  u16 boneOffset
  u16 triangleOffset
hclSimpleMeshBoneDeformOperator : hclOperator
  u32 inputBufferIdx
  u32 outputTransformSetIdx
  array<hclSimpleMeshBoneDeformOperatorTriangleBonePair> triangleBonePairs
  array<mat4> localBoneTransforms
hclCopyVerticesOperator : hclOperator
  u32 inputBufferIdx
  u32 outputBufferIdx
  u32 numberOfVertices
  u32 startVertexIn
  u32 startVertexOut
  bool copyNormals
hclGatherAllVerticesOperator : hclOperator
  array<s16> vertexInputFromVertexOutput
  u32 inputBufferIdx
  u32 outputBufferIdx
  bool gatherNormals
  bool partialWrite
hkPackedVector3
  s16[4] values
hclObjectSpaceDeformerEightBlendEntryBlock
  u16[16] vertexIndices
  u16[128] boneIndices
  u8[128] boneWeights
hclObjectSpaceDeformerSevenBlendEntryBlock
  u16[16] vertexIndices
  u16[112] boneIndices
  u8[112] boneWeights
hclObjectSpaceDeformerSixBlendEntryBlock
  u16[16] vertexIndices
  u16[96] boneIndices
  u8[96] boneWeights
hclObjectSpaceDeformerFiveBlendEntryBlock
  u16[16] vertexIndices
  u16[80] boneIndices
  u8[80] boneWeights
hclObjectSpaceDeformerFourBlendEntryBlock
  u16[16] vertexIndices
  u16[64] boneIndices
  u8[64] boneWeights
hclObjectSpaceDeformerThreeBlendEntryBlock
  u16[16] vertexIndices
  u16[48] boneIndices
  u8[48] boneWeights
hclObjectSpaceDeformerTwoBlendEntryBlock
  u16[16] vertexIndices
  u16[32] boneIndices
  u8[32] boneWeights
hclObjectSpaceDeformerOneBlendEntryBlock
  u16[16] vertexIndices
  u16[16] boneIndices
hclObjectSpaceDeformerLocalBlockP
  hkPackedVector3[16] localPosition
hclObjectSpaceDeformerLocalBlockUnpackedP
  vec4[16] localPosition
hclObjectSpaceDeformer
  array<hclObjectSpaceDeformerEightBlendEntryBlock> eightBlendEntries
  array<hclObjectSpaceDeformerSevenBlendEntryBlock> sevenBlendEntries
  array<hclObjectSpaceDeformerSixBlendEntryBlock> sixBlendEntries
  array<hclObjectSpaceDeformerFiveBlendEntryBlock> fiveBlendEntries
  array<hclObjectSpaceDeformerFourBlendEntryBlock> fourBlendEntries
  array<hclObjectSpaceDeformerThreeBlendEntryBlock> threeBlendEntries
  array<hclObjectSpaceDeformerTwoBlendEntryBlock> twoBlendEntries
  array<hclObjectSpaceDeformerOneBlendEntryBlock> oneBlendEntries
  array<u8> controlBytes
  u16 startVertexIndex
  u16 endVertexIndex
  u16 batchSizeSpu
  bool partialWrite
hclObjectSpaceSkinOperator : hclOperator
  array<mat4> boneFromSkinMeshTransforms
  array<u16> transformSubset
  u32 outputBufferIndex
  u32 transformSetIndex
  hclObjectSpaceDeformer objectSpaceDeformer
hclObjectSpaceSkinPOperator : hclObjectSpaceSkinOperator
  array<hclObjectSpaceDeformerLocalBlockP> localPs
  array<hclObjectSpaceDeformerLocalBlockUnpackedP> localUnpackedPs
hclBoneSpaceDeformerFourBlendEntryBlock
  u16[4] vertexIndices
  u16[16] boneIndices
hclBoneSpaceDeformerThreeBlendEntryBlock
  u16[5] vertexIndices
  u16[15] boneIndices
hclBoneSpaceDeformerTwoBlendEntryBlock
  u16[8] vertexIndices
  u16[16] boneIndices
hclBoneSpaceDeformerOneBlendEntryBlock
  u16[16] vertexIndices
  u16[16] boneIndices
hclBoneSpaceDeformerLocalBlockP
  vec4[16] localPosition
hclBoneSpaceDeformerLocalBlockUnpackedP
  vec4[16] localPosition
hclBoneSpaceDeformer
  array<hclBoneSpaceDeformerFourBlendEntryBlock> fourBlendEntries
  array<hclBoneSpaceDeformerThreeBlendEntryBlock> threeBlendEntries
  array<hclBoneSpaceDeformerTwoBlendEntryBlock> twoBlendEntries
  array<hclBoneSpaceDeformerOneBlendEntryBlock> oneBlendEntries
  array<u8> controlBytes
  u16 startVertexIndex
  u16 endVertexIndex
  u16 batchSizeSpu
  bool partialWrite
hclBoneSpaceSkinOperator : hclOperator
  array<u16> transformSubset
  u32 outputBufferIndex
  u32 transformSetIndex
  hclBoneSpaceDeformer boneSpaceDeformer
hclBoneSpaceSkinPOperator : hclBoneSpaceSkinOperator
  array<hclBoneSpaceDeformerLocalBlockP> localPs
  array<hclBoneSpaceDeformerLocalBlockUnpackedP> localUnpackedPs
hclClothStateBufferAccessBufferUsage
  u8[4] perComponentFlags
  bool trianglesRead
hclClothStateBufferAccess
  u32 bufferIndex
  hclClothStateBufferAccessBufferUsage bufferUsage
  u32 shadowBufferIndex
hkBitField
  array<u32> words
  s32 numBits
hclTransformSetUsageTransformTracker
  hkBitField read
  hkBitField readBeforeWrite
  hkBitField written
hclTransformSetUsage
  u8[2] perComponentFlags
  array<hclTransformSetUsageTransformTracker> perComponentTransformTrackers
hclClothStateTransformSetAccess
  u32 transformSetIndex
  hclTransformSetUsage transformSetUsage
hclClothState : hkReferencedObject
  str name
  array<u32> operators
  array<hclClothStateBufferAccess> usedBuffers
  array<hclClothStateTransformSetAccess> usedTransformSets
  array<u32> usedSimCloths
hclClothData : hkReferencedObject
  str name
  array<ptr<hclSimClothData>> simClothDatas
  array<ptr<hclBufferDefinition>> bufferDefinitions
  array<ptr<hclTransformSetDefinition>> transformSetDefinitions
  array<ptr<hclOperator>> operators
  array<ptr<hclClothState>> clothStateDatas
  array<ptr> actions
  u32 targetPlatform
hclClothContainer : hkReferencedObject
  array<ptr<hclCollidable>> collidables
  array<ptr<hclClothData>> clothDatas

hkcdStaticTreeDefaultTreeStorage6 : hkcdStaticTreeTreeDynamicStorage6
hkcdStaticAabbTree : hkReferencedObject
  bool shouldDeleteTree nosave
  ptr<hkcdStaticTreeDefaultTreeStorage6> treePtr
hkaiNavMeshQueryMediator : hkReferencedObject
hkaiStaticTreeNavMeshQueryMediator : hkaiNavMeshQueryMediator
  ptr<hkcdStaticAabbTree> tree
  ptr<hkaiNavMesh> navMesh
hkaiStreamingSetNavMeshConnection
  s32 faceIndex
  s32 edgeIndex
  s32 oppositeFaceIndex
  s32 oppositeEdgeIndex
hkaiStreamingSetGraphConnection
  s32 nodeIndex
  s32 oppositeNodeIndex
  u32 edgeData
  half edgeCost
  u16 edgeFlags
hkaiStreamingSetVolumeConnection
  s32 cellIndex
  s32 oppositeCellIndex
hkaiStreamingSet
  u32 thisUid
  u32 oppositeUid
  array<hkaiStreamingSetNavMeshConnection> meshConnections
  array<hkaiStreamingSetGraphConnection> graphConnections
  array<hkaiStreamingSetVolumeConnection> volumeConnections
hkaiNavMeshFace
  s32 startEdgeIndex
  s32 startUserEdgeIndex
  s16 numEdges
  s16 numUserEdges
  s16 clusterIndex
  u16 padding
hkaiNavMeshEdge
  s32 a
  s32 b
  u32 oppositeEdge
  u32 oppositeFace
  u8 flags
  u8 paddingByte
  half userEdgeCost
hkaiNavMesh : hkReferencedObject
  array<hkaiNavMeshFace> faces
  array<hkaiNavMeshEdge> edges
  array<vec4> vertices
  array<hkaiStreamingSet> streamingSets
  array<s32> faceData
  array<s32> edgeData
  s32 faceDataStriding
  s32 edgeDataStriding
  u8 flags
  hkAabb aabb
  f32 erosionRadius
  ulong userData
hkaiDirectedGraphExplicitCostNode
  s32 startEdgeIndex
  s32 numEdges
hkaiDirectedGraphExplicitCostEdge
  half cost
  u16 flags
  u32 target
hkaiDirectedGraphExplicitCost : hkReferencedObject
  array<vec4> positions
  array<hkaiDirectedGraphExplicitCostNode> nodes
  array<hkaiDirectedGraphExplicitCostEdge> edges
  array<s32> nodeData
  array<s32> edgeData
  s32 nodeDataStriding
  s32 edgeDataStriding
  array<hkaiStreamingSet> streamingSets

StaticCompoundInfoActorInfo
  u32 hashId
  s32 srtHash
  s32 shapeInfoStart
  s32 shapeInfoEnd
StaticCompoundInfoShapeInfo
  s32 actorInfoIndex
  s32 instanceId
  s8 bodyGroup
  u8 bodyLayerType
StaticCompoundInfo
  u32 offset
  array<StaticCompoundInfoActorInfo> actorInfo
  array<StaticCompoundInfoShapeInfo> shapeInfo
""";
}
