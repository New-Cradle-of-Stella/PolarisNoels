using m2d;
using nel;
using System.Collections;
using UnityEngine;
using XX;

namespace PolarisNoels.SN
{
    public class ShadowNoelNickname : M2Attackable
    {
        private PRMain followTarget;
        private TextRenderer Tx;
        private TextRenderer TxBg;
        private GameObject GobTx;
        public Vector2 followOffset = new(0f, -1.5f);
        public float followLerp = 0.15f;
        public bool immediateFollow = false;
        private string currentText = "";
        private bool IsMsg;
        private int currentTextLength
        {
            get
            {
                bool count = true;
                int counter = 0;
                foreach (char c in currentText)
                {
                    if (c == '<')
                    {
                        count = false;
                    }
                    else if (c == '/')
                    {
                        count = true;
                        counter--;
                    }
                    if (count)
                    {
                        counter++;
                    }
                }
                return counter;
            }
        }
        private bool textVisible = true;
        private Color32 txColor = MTRX.ColWhite;
        private Color32 bgColor = new(0, 0, 0, 1);
        private float txSize = 18f;
        private ALIGN txAlign = ALIGN.CENTER;
        private ALIGNY txAlignY = ALIGNY.MIDDLE;
        private float txOffsetPixelX = 0f;
        private float txOffsetPixelY = -40f;

        public override HITTYPE getHitType(M2Ray Ray)
        {
            return HITTYPE.NONE;
        }
        
        public override RAYHIT can_hit(M2Ray Ray)
        {
            return RAYHIT.NONE;
        }
        
        public override bool isDamagingOrKo()
        {
            return false;
        }

        public override void appear(Map2d Mp)
        {
            base.appear(Mp);
            this.floating = true;
            this.base_gravity = 0f;
            base.gameObject.layer = 2;
            base.carryable_other_object = false;
        }

        /// <summary>在目标头顶创建昵称标签。</summary>
        public static ShadowNoelNickname CreateNickname(PRMain target, string nickname)
        {
            ShadowNoelNickname label = Spawn(target, $"Nickname_{nickname}");
            label.SetText(nickname);
            label.SetTextOffset(0f, -50f);
            return label;
        }

        /// <summary>在目标头顶创建短消息气泡（无底色，初始为空）。</summary>
        public static ShadowNoelNickname CreateMessageBubble(PRMain target)
        {
            ShadowNoelNickname bubble = Spawn(target, $"Msg_{target}");
            bubble.SetTextOffset(0f, 0f);
            bubble.SetBgColor(new(0, 0, 0, 0));
            return bubble;
        }

        static ShadowNoelNickname Spawn(PRMain target, string name)
        {
            Map2d mp = target.Mp;
            target.getPosition(out float x, out float y);
            ShadowNoelNickname follower = mp.createMover<ShadowNoelNickname>(name, x, y);
            follower.SetFollowTarget(target, new Vector2(0f, -2f));
            follower.SetTextSize(20f);
            follower.SetTextColor(uint.MaxValue);
            follower.SetAlpha(1);
            mp.assignMover(follower);
            follower.appear(mp);
            return follower;
        }

        public void SetFollowTarget(PRMain target, Vector2? offset = null)
        {
            this.followTarget = target;
            if (offset.HasValue)
            {
                this.followOffset = offset.Value;
            }
            if (target != null && this.Mp != null)
            {
                this.setTo(
                    target.x + followOffset.x,
                    target.y + followOffset.y
                );
            }
        }

        public void SetText(string text)
        {
            if (this.currentText == text) return;
            this.currentText = text ?? "";
            if (string.IsNullOrEmpty(this.currentText))
            {
                HideText();
                return;
            }
            EnsureTextRenderer();
            this.Tx.html_mode = true;
            this.Tx.Txt(this.currentText);
            if (this.textVisible)
            {
                this.GobTx.SetActive(true);
            }
        }

        public void ShowText()
        {
            this.textVisible = true;
            if (this.GobTx != null && !string.IsNullOrEmpty(this.currentText))
            {
                this.GobTx.SetActive(true);
            }
        }

        public void HideText()
        {
            this.textVisible = false;
            this.GobTx?.SetActive(false);
        }

        public void SetTextColor(Color32 color)
        {
            return;
            this.txColor = color;
            this.Tx?.Col(color);
        }

        public void SetTextColor(uint color)
        {
            this.SetTextColor(C32.d2c(color));
        }

        public void SetTextSize(float size)
        {
            this.txSize = size;
            this.Tx?.Size(size);
        }

        public void SetTextOffset(float pixelX, float pixelY)
        {
            this.txOffsetPixelX = pixelX;
            this.txOffsetPixelY = pixelY;
        }

        public void SetAlpha(float alpha)
        {
            this.Tx?.Alpha(alpha);
        }

        public string GetCurrentText()
        {
            return this.currentText;
        }

        private void EnsureTextRenderer()
        {
            if (this.Tx != null) return;
            this.GobTx = IN.CreateGob(gameObject, "tx", false);
            this.GobTx.layer = 25; //25 is GUI layer
            this.Tx = this.GobTx.AddComponent<TextRenderer>();
            this.Tx.html_mode = true;
            this.Tx.auto_condense = true;
            this.Tx.Col(this.txColor);
            this.Tx.BorderCol(new Color(0, 0, 0, 1));
            this.Tx.Size(this.txSize);
            this.Tx.Align(this.txAlign).AlignY(this.txAlignY);
            IN.setZ(this.GobTx.transform, -1f);
            var bg = IN.CreateGob(gameObject, "bg", false);
            bg.layer = 25;
            this.TxBg = bg.AddComponent<TextRenderer>();
            this.TxBg.setText(new STB("┃"));
            this.TxBg.Col(this.bgColor);
            this.TxBg.BorderCol(new Color(0, 0, 0, 0));
            this.TxBg.Size(40);
            this.TxBg.Alpha(0.4f);
            this.TxBg.Align(ALIGN.CENTER);
            this.TxBg.transform.localPosition += new Vector3(0, -0.5f);
            float factor = currentTextLength * 1.3461f + 2.7667f;
            this.TxBg.transform.localScale = new(factor, 1, 1);
            IN.setZ(this.TxBg.transform, -0.9f);
        }

        private void UpdateTextPosition()
        {
            if (this.Tx == null || this.Mp == null) return;
            float px = this.txOffsetPixelX * 0.015625f;
            float py = this.txOffsetPixelY * 0.015625f;
            Vector3 pos = this.GobTx.transform.localPosition;
            pos.x = px;
            pos.y = py;
            this.GobTx.transform.localPosition = pos;
        }

        public override void runPre()
        {
            base.runPre();
            if (base.destructed) return;
            if (this.followTarget != null && this.followTarget.destructed)
            {
                this.followTarget = null;
                HideText();
                return;
            }
            if (this.followTarget == null) return;
            Vector3 delta = GameObject.Find("CameraContainer").transform.position;
            if (GameObject.Find("CameraFinalRender to GUI") is not GameObject gameObject)
            {
                return;
            }
            Vector3 delta1 = gameObject.transform.position;
            float targetX = this.followTarget.x + this.followOffset.x - delta.x * 1.15f + delta1.x * 1.15f;
            float targetY = this.followTarget.y + this.followOffset.y + delta.y * 1.15f + delta1.y * 1.15f;
            Vector2 off = new(0, -1);
            targetX += off.x;
            targetY += off.y;
            if (this.immediateFollow)
            {
                if (base.x != targetX || base.y != targetY)
                {
                    this.setTo(targetX, targetY);
                }
            }
            else
            {
                float newX = X.NI(base.x, targetX, this.followLerp);
                float newY = X.NI(base.y, targetY, this.followLerp);
                float dx = newX - base.x;
                float dy = newY - base.y;
                if (X.Abs(dx) > 0.001f || X.Abs(dy) > 0.001f)
                {
                    this.moveBy(dx, dy, false);
                }
            }
        }

        public override void runPost()
        {
            base.runPost();
            if (base.destructed) return;
            if (this.Tx != null && this.GobTx.activeSelf && this.textVisible)
            {
                UpdateTextPosition();
            }
            if (CFGMultiplayer.showNicknames)
            {
                ShowText();
            }
            else
            {
                HideText();
            }
            if (followTarget is ShadowNoel noel && Tx != null)
            {
                SetBgColor((Color)DB.partyInfos[noel.PartyID].Color);
                if (CFGMultiplayer.showDelay && DB.peerDelays.ContainsKey(noel.ID) && !IsMsg)
                {
                    this.Tx.Txt(this.currentText + " - " + DB.peerDelays[noel.ID] + "ms");
                }
                else
                {
                    this.Tx.Txt(this.currentText);
                }
            }
            else if (followTarget is PRNoel)
            {
                SetBgColor((Color)DB.partyInfos[DB.LocalNoelParty].Color);
            }
        }

        public override void destruct()
        {
            if (base.destructed) return;
            this.Tx?.OnDestroy();
            this.Tx = null;
            if (this.GobTx != null)
            {
                IN.DestroyOne(this.GobTx);
                this.GobTx = null;
            }
            this.followTarget = null;
            base.destruct();
        }

        public void SetBgColor(Color color)
        {
            this.bgColor = color;
            this.TxBg?.Col(color);
        }

        private Coroutine Msg;

        public void ShowMsg(string txtID)
        {
            IsMsg = true;
            if (Msg != null)
            {
                StopCoroutine(Msg);
                Msg = null;
            }
            Msg = StartCoroutine(OnMsg(txtID));
        }

        public IEnumerator OnMsg(string txtID)
        {
            string text = TX.Get(txtID);
            for (int i = 0; i < text.Length; i++)
            {
                SetText(text[..(i + 1)]);
                ShowText();
                yield return new WaitForSecondsRealtime(0.25f);
            }
            yield return new WaitForSecondsRealtime(2f);
            SetText("");
            Msg = null;
        }
    }
}
