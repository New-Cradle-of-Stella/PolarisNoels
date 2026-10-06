using nel.title;
using UnityEngine;
using XX;

namespace WeNeedMoreNoels
{
    public class UITitleMultiplayerConfirm : UiTitleDifficultyConfirm
    {
        public UITitleMultiplayerConfirm(GameObject _Base, float _z, SceneTitleTemp _Title, int def_cursor = 0, int _cursor_max = 2) : base(_Base, _z, _Title, def_cursor, _cursor_max)
        {
        }

        public override int prepareMesh(MeshDrawer Md, int start_id)
        {
            MTI mti = MTI.LoadContainer("WNMNResources\\multiplayer");
            MImage image = mti.LoadImage("multiplayer");
            Md.chooseSubMesh(start_id, false, false);
            Md.setMaterial(image.getMtr(BLEND.NORMAL, -1), false);
            return 1;
        }

        public override void drawScrollPicture(int i, float cx, float cy, float scale)
        {
            this.Md.chooseSubMesh(1);
            this.Md.initForImg(this.Title.MIdifficulty.Tx);
            float num = 0.5f;
            this.Md.uvRect(num * (float)i, 0f, num, 1f, false, false).RotaGraph(cx + 150, cy, scale * 0.8f, 0f, null, false);
        }

        public override void fineText(int cursor)
        {
            FbT.text_content = TX.Get("Title_multiplayer_top", "");
            FbB.text_content = TX.Get((cursor == 0) ? "Title_multiplayer_desc_host" : "Title_multiplayer_desc_client", "");
            FbC.text_content = TX.Get((cursor == 0) ? "Title_multiplayer_host" : "Title_multiplayer_client", "");
        }

        public override bool isDecided(out int _result)
        {
            _result = result;
            if (result >= 0)
            {
                DB.WNMNEnterNetworkType = (NetWorkType)diff_cursor;
                DB.WNMNEnterNetworkTypeSelected = true;
                SND.Ui.play("enter");
            }
            return false;
        }

        public override string ToString()
        {
            return "<UiTitleMultiplayerConfirm>";
        }
    }
}
