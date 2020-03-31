﻿// ----------------------------------------------------------------------------
// VRTK_DestinationPoint4VRWS.cs
// This script is a modification of VRTK_DestinationPoint.cs.
// ----------------------------------------------------------------------------


// Destination Point|Prefabs|0090
namespace VRTK
{
    using UnityEngine;
    using System.Collections;
    using UniRx;
    using System;


    /// <summary>
    /// Allows for a specific scene marker or specific area within the scene that can be teleported to.
    /// </summary>
    /// <remarks>
    /// **Prefab Usage:**
    ///  * Place the `VRTK/Prefabs/DestinationPoint/DestinationPoint` prefab at the desired location within the scene.
    ///  * Uncheck the `Enable Teleport` checkbox to lock the destination point and prevent teleporting to it.
    ///  * Uncheck the `Snap To Point` checkbox to provide a destination area rather than a specific point to teleport to.
    /// </remarks>
    /// <example>
    /// `044_CameraRig_RestrictedTeleportZones` uses the `VRTK_DestinationPoint` prefab to set up a collection of pre-defined teleport locations.
    /// </example>
    public class VRTK_DestinationPoint4VRWS : VRTK_DestinationMarker
    {
        /// <summary>
        /// Allowed snap to rotation types.
        /// </summary>
        public enum RotationTypes
        {
            /// <summary>
            /// No rotation information will be emitted in the destination set payload.
            /// </summary>
            NoRotation,
            /// <summary>
            /// The destination point's rotation will be emitted without taking into consideration the current headset rotation.
            /// </summary>
            RotateWithNoHeadsetOffset,
            /// <summary>
            /// The destination point's rotation will be emitted and will take into consideration the current headset rotation.
            /// </summary>
            RotateWithHeadsetOffset
        }

        [Header("Destination Point Settings")]

        [Tooltip("The GameObject to use to represent the default cursor state.")]
        public GameObject defaultCursorObject;
        [Tooltip("The GameObject to use to represent the hover cursor state.")]
        public GameObject hoverCursorObject;
        [Tooltip("The GameObject to use to represent the locked cursor state.")]
        public GameObject lockedCursorObject;
        [Tooltip("An optional transform to determine the destination location for the destination marker. This can be useful to offset the destination location from the destination point. If this is left empty then the destiantion point transform will be used.")]
        public Transform destinationLocation;
        [Tooltip("If this is checked, then the pointer cursor will be hidden when a valid destination point is hovered over.")]
        public bool hidePointerCursorOnHover = true;
        [Tooltip("If this is checked, then the pointer direction indicator will be hidden when a valid destination point is hovered over. A pointer direction indicator will always be hidden if snap to rotation is set.")]
        public bool hideDirectionIndicatorOnHover = false;
        [Tooltip("Determines if the play area will be rotated to the rotation of the destination point upon the destination marker being set.")]
        public RotationTypes snapToRotation = RotationTypes.NoRotation;

        [Header("Custom Settings")]

        public static VRTK_DestinationPoint4VRWS currentDestinationPoint;


        protected Collider pointCollider;
        protected bool createdCollider;
        protected Rigidbody pointRigidbody;
        protected bool createdRigidbody;
        protected Coroutine initaliseListeners;
        protected bool isActive;
        protected VRTK_BasePointerRenderer.VisibilityStates storedCursorState;
        protected bool storedDirectionIndicatorState;
        protected Transform playArea;
        protected Transform headset;

        private static Subject<GameObject> m_OnClickSeat = new Subject<GameObject>();
        public static IObservable<GameObject> OnClickSeat { get { return m_OnClickSeat; } }

        /// <summary>
        /// The ResetDestinationPoint resets the destination point back to the default state.
        /// </summary>
        public virtual void ResetDestinationPoint()
        {
            ResetPoint();
        }

        protected virtual void Awake()
        {
            VRTK_SDKManager.AttemptAddBehaviourToToggleOnLoadedSetupChange(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            CreateColliderIfRequired();
            SetupRigidbody();
            initaliseListeners = StartCoroutine(ManageDestinationMarkersAtEndOfFrame());
            ResetPoint();
            playArea = VRTK_DeviceFinder.PlayAreaTransform();
            headset = VRTK_DeviceFinder.HeadsetTransform();
            destinationLocation = (destinationLocation != null ? destinationLocation : transform);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (initaliseListeners != null)
            {
                StopCoroutine(initaliseListeners);
            }

            ManageDestinationMarkers(false);
            if (createdCollider)
            {
                Destroy(pointCollider);
                pointCollider = null;
            }

            if (createdRigidbody)
            {
                Destroy(pointRigidbody);
                pointRigidbody = null;
            }


        }

        protected virtual void OnDestroy()
        {
            VRTK_SDKManager.AttemptRemoveBehaviourToToggleOnLoadedSetupChange(this);
        }


        protected virtual void CreateColliderIfRequired()
        {
            pointCollider = GetComponentInChildren<Collider>();
            createdCollider = false;
            if (pointCollider == null)
            {
                pointCollider = gameObject.AddComponent<SphereCollider>();
                createdCollider = true;
            }

            pointCollider.isTrigger = true;
        }

        protected virtual void SetupRigidbody()
        {
            pointRigidbody = GetComponent<Rigidbody>();
            createdRigidbody = false;
            if (pointRigidbody == null)
            {
                pointRigidbody = gameObject.AddComponent<Rigidbody>();
                createdRigidbody = true;
            }
            pointRigidbody.isKinematic = true;
            pointRigidbody.useGravity = false;
        }

        protected virtual IEnumerator ManageDestinationMarkersAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            if (enabled)
            {
                ManageDestinationMarkers(true);
            }

        }

        protected virtual void ManageDestinationMarkers(bool state)
        {
            ManageDestinationMarkerListeners(VRTK_DeviceFinder.GetControllerLeftHand(), state);
            ManageDestinationMarkerListeners(VRTK_DeviceFinder.GetControllerRightHand(), state);

            for (int i = 0; i < VRTK_ObjectCache.registeredDestinationMarkers.Count; i++)
            {
                VRTK_DestinationMarker destinationMarker = VRTK_ObjectCache.registeredDestinationMarkers[i];
                ManageDestinationMarkerListeners(destinationMarker.gameObject, state);
            }
        }

        protected virtual void ManageDestinationMarkerListeners(GameObject markerMaker, bool register)
        {
            if (markerMaker != null)
            {
                VRTK_DestinationMarker[] worldMarkers = markerMaker.GetComponentsInChildren<VRTK_DestinationMarker>();
                for (int i = 0; i < worldMarkers.Length; i++)
                {
                    VRTK_DestinationMarker worldMarker = worldMarkers[i];
                    if (worldMarker == this)
                    {
                        continue;
                    }
                    if (register)
                    {
                        worldMarker.DestinationMarkerEnter += DoDestinationMarkerEnter;
                        worldMarker.DestinationMarkerExit += DoDestinationMarkerExit;
                        worldMarker.DestinationMarkerSet += DoDestinationMarkerSet;
                    }
                    else
                    {
                        worldMarker.DestinationMarkerEnter -= DoDestinationMarkerEnter;
                        worldMarker.DestinationMarkerExit -= DoDestinationMarkerExit;
                        worldMarker.DestinationMarkerSet -= DoDestinationMarkerSet;
                    }
                }
            }
        }

        protected virtual void DoDestinationMarkerEnter(object sender, DestinationMarkerEventArgs e)
        {
            if (!isActive && e.raycastHit.transform == transform)
            {
                if (destinationLocation.childCount == 1)
                {
                    isActive = true;
                    ToggleCursor(sender, false);
                    EnablePoint();
                    OnDestinationMarkerEnter(SetDestinationMarkerEvent(0f, e.raycastHit.transform, e.raycastHit, e.raycastHit.transform.position, e.controllerReference, false, GetRotation()));
                }

            }
        }

        protected virtual void DoDestinationMarkerExit(object sender, DestinationMarkerEventArgs e)
        {
            if (isActive && e.raycastHit.transform == transform)
            {
                isActive = false;
                ToggleCursor(sender, true);
                ResetPoint();
                OnDestinationMarkerExit(SetDestinationMarkerEvent(0f, e.raycastHit.transform, e.raycastHit, e.raycastHit.transform.position, e.controllerReference, false, GetRotation()));
            }
        }

        protected virtual void DoDestinationMarkerSet(object sender, DestinationMarkerEventArgs e)
        {
            if (e.raycastHit.transform == transform)
            {
				// 1度のClickで何故か2回ここに入っていたので、以下を追加して弾くようにした
				if( currentDestinationPoint == this )
					return;

                currentDestinationPoint = this;

				m_OnClickSeat.OnNext(destinationLocation.gameObject);

                DisablePoint();
            }
            else if (currentDestinationPoint != this)
            {
                ResetPoint();
            }
            else if (currentDestinationPoint != null && e.raycastHit.transform != currentDestinationPoint.transform)
            {
                currentDestinationPoint = null;
                ResetPoint();
            }
        }

        protected virtual void ToggleCursor(object sender, bool state)
        {
            if ((hidePointerCursorOnHover || hideDirectionIndicatorOnHover) && sender.GetType() == typeof(VRTK_Pointer))
            {
                VRTK_Pointer pointer = (VRTK_Pointer)sender;
                if (pointer != null && pointer.pointerRenderer != null)
                {
                    TogglePointerCursor(pointer.pointerRenderer, state);
                    ToggleDirectionIndicator(pointer.pointerRenderer, state);
                }
            }
        }

        protected virtual void TogglePointerCursor(VRTK_BasePointerRenderer pointerRenderer, bool state)
        {
            if (hidePointerCursorOnHover)
            {
                if (!state)
                {
                    storedCursorState = pointerRenderer.cursorVisibility;
                    pointerRenderer.cursorVisibility = VRTK_BasePointerRenderer.VisibilityStates.AlwaysOff;
                }
                else
                {
                    pointerRenderer.cursorVisibility = storedCursorState;
                }
            }
        }

        protected virtual void ToggleDirectionIndicator(VRTK_BasePointerRenderer pointerRenderer, bool state)
        {
            if (pointerRenderer.directionIndicator != null && hideDirectionIndicatorOnHover)
            {
                if (!state)
                {
                    storedDirectionIndicatorState = pointerRenderer.directionIndicator.isActive;
                    pointerRenderer.directionIndicator.isActive = false;
                }
                else
                {
                    pointerRenderer.directionIndicator.isActive = storedDirectionIndicatorState;
                }
            }
        }

        protected virtual void EnablePoint()
        {
            ToggleObject(lockedCursorObject, false);
            ToggleObject(defaultCursorObject, false);
            ToggleObject(hoverCursorObject, true);
        }

        protected virtual void SetColliderState(bool state)
        {
            if (pointCollider != null)
            {
                pointCollider.enabled = state;
            }
        }

        protected virtual void DisablePoint()
        {
            SetColliderState(false);
            ToggleObject(lockedCursorObject, false);
            ToggleObject(defaultCursorObject, false);
            ToggleObject(hoverCursorObject, false);
        }

        protected virtual void ResetPoint()
        {
            if (currentDestinationPoint == this)
            {
                return;
            }

            ToggleObject(hoverCursorObject, false);
            if (enableTeleport)
            {
                SetColliderState(true);
                ToggleObject(defaultCursorObject, true);
                ToggleObject(lockedCursorObject, false);
            }
            else
            {
                SetColliderState(false);
                ToggleObject(lockedCursorObject, true);
                ToggleObject(defaultCursorObject, false);
            }
        }

        protected virtual void ToggleObject(GameObject givenObject, bool state)
        {
            if (givenObject != null)
            {
                givenObject.SetActive(state);
            }
        }

        protected virtual Quaternion? GetRotation()
        {
            if (snapToRotation == RotationTypes.NoRotation)
            {
                return null;
            }

            float offset = (snapToRotation == RotationTypes.RotateWithHeadsetOffset && playArea != null && headset != null ? playArea.eulerAngles.y - headset.eulerAngles.y : 0f);
            return Quaternion.Euler(0f, destinationLocation.eulerAngles.y + offset, 0f);
        }
    }
}