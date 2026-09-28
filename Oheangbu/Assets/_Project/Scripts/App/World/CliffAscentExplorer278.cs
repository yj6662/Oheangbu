using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World
{
    // Standalone art prototype only. No campaign, inventory, or save dependency.
    [RequireComponent(typeof(CharacterController))]
    public sealed class CliffAscentExplorer278 : MonoBehaviour
    {
        public Camera View;
        public Vector3 StartFeet;
        public bool Flying;
        float yaw, pitch, velocityY;
        CharacterController controller;
        void Awake()
        {
            controller=GetComponent<CharacterController>();yaw=transform.eulerAngles.y;
#if UNITY_EDITOR
            if(UnityEditor.SessionState.GetBool("Ascent278.Probe",false)){enabled=false;return;}
#endif
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
        }
        void OnDisable(){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        void Update()
        {
            var key=Keyboard.current;var mouse=Mouse.current;if(key==null||mouse==null||View==null)return;
            if(key.escapeKey.wasPressedThisFrame){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            if(mouse.leftButton.wasPressedThisFrame){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
            if(key.tabKey.wasPressedThisFrame){Flying=!Flying;velocityY=0;}
            if(key.rKey.wasPressedThisFrame||transform.position.y<-140)ResetFeet();
            if(Cursor.lockState!=CursorLockMode.Locked)return;
            var delta=mouse.delta.ReadValue();yaw+=delta.x*.09f;pitch=Mathf.Clamp(pitch-delta.y*.09f,-80,80);
            transform.rotation=Quaternion.Euler(0,yaw,0);View.transform.localRotation=Quaternion.Euler(pitch,0,0);
            var input=new Vector2((key.dKey.isPressed?1:0)-(key.aKey.isPressed?1:0),(key.wKey.isPressed?1:0)-(key.sKey.isPressed?1:0));
            var move=Vector3.ClampMagnitude(transform.right*input.x+transform.forward*input.y,1);
            if(Flying)
            {
                controller.enabled=false;
                move.y=(key.eKey.isPressed?1:0)-(key.qKey.isPressed?1:0);
                transform.position+=move*(key.leftShiftKey.isPressed?28:12)*Time.deltaTime;
            }
            else
            {
                if(!controller.enabled)controller.enabled=true;
                if(controller.isGrounded&&velocityY<0)velocityY=-3;
                velocityY=Mathf.Max(-40,velocityY-22*Time.deltaTime);
                controller.Move((move*(key.leftShiftKey.isPressed?6.5f:4.5f)+Vector3.up*velocityY)*Time.deltaTime);
            }
        }
        public void ResetFeet(){if(controller==null)controller=GetComponent<CharacterController>();controller.enabled=false;transform.position=StartFeet+Vector3.up*.12f;controller.enabled=true;Flying=false;velocityY=0;}
    }
}
