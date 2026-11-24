using System.IO;
using Chase.Online;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Chase.EditorTools
{
    // 금고 다이얼 임시 UI 생성기. 메뉴: Chase > Build Safe Dial UI
    // 임시 그림(숫자판·원·삼각형)을 코드로 그려 PNG 로 저장하고, 전체 창·작은 다이얼 프리팹을 만든다.
    // 디자인 담당 시안이 나오면 프리팹의 이미지만 교체하면 된다.
    public static class SafeDialUIBuilder
    {
        const string Dir = "Assets/Chase/Prefabs/UI";
        const string FullPath = Dir + "/SafeDialFull.prefab";
        const string CompactPath = Dir + "/SafeDialCompact.prefab";

        static readonly Color Panel = new Color32(0x15, 0x18, 0x1D, 0xF2);
        static readonly Color Gold = new Color32(0xE0, 0xA8, 0x2E, 0xFF);
        static readonly Color Muted = new Color32(0xCF, 0xD3, 0xDA, 0xFF);

        [MenuItem("Chase/Build Safe Dial UI")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var face = SaveSprite("SafeDialFace", DrawFace(512));
            var circle = SaveSprite("SafeDialCircle", DrawCircle(128, 0f));
            var ring = SaveSprite("SafeDialRing", DrawCircle(256, 0.9f));
            var tri = SaveSprite("SafeDialPointer", DrawTriangle(64));

            BuildPrefab(FullPath, true, face, circle, ring, tri);
            BuildPrefab(CompactPath, false, face, circle, ring, tri);
            AssetDatabase.SaveAssets();
            Debug.Log("[SafeDialUIBuilder] 프리팹 생성 완료: " + FullPath + ", " + CompactPath);
        }

        // ---------------- 프리팹 ----------------

        static void BuildPrefab(string path, bool full, Sprite faceSprite, Sprite circle, Sprite ring, Sprite tri)
        {
            var root = new GameObject(full ? "SafeDialFull" : "SafeDialCompact", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            SafeDialViewBase view = full ? root.AddComponent<SafeDialFullView>() : (SafeDialViewBase)root.AddComponent<SafeDialCompactView>();

            if (full)
            {
                var dim = Img(root.transform, "Dim", null, new Color(0, 0, 0, 0.6f));
                Stretch(dim.rectTransform);
            }

            // 작은 다이얼: 배경 사각형 없이 다이얼과 램프만 (패널은 위치 잡는 용도로만, 투명)
            float dial = full ? 430f : 170f;
            Vector2 panelSize = full ? new Vector2(1150, 650) : new Vector2(250, 210);
            var panel = Img(root.transform, "Panel", null, full ? Panel : Color.clear);
            panel.rectTransform.sizeDelta = panelSize;
            if (full)
            {
                var outline = panel.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1, 1, 1, 0.12f);
                outline.effectDistance = new Vector2(2, -2);
            }
            view.panel = panel.rectTransform;

            Vector2 dialCenter = full ? new Vector2(0, 25) : new Vector2(-30, -10);

            var hint = Img(panel.transform, "HintRing", ring, Gold);
            hint.rectTransform.sizeDelta = Vector2.one * (dial + (full ? 26 : 10));
            hint.rectTransform.anchoredPosition = dialCenter;
            hint.enabled = false;
            view.hintRing = hint;

            var faceImg = Img(panel.transform, "Face", faceSprite, Color.white);
            faceImg.rectTransform.sizeDelta = Vector2.one * dial;
            faceImg.rectTransform.anchoredPosition = dialCenter;
            view.face = faceImg.rectTransform;
            view.faceImage = faceImg;
            // 숫자 0,10,...,90 은 판과 함께 돌도록 자식으로
            {
                for (int n = 0; n < 100; n += 10)
                {
                    float a = -n * 3.6f * Mathf.Deg2Rad; // 시계 방향으로 증가
                    var num = Txt(faceImg.transform, "N" + n, n.ToString(), full ? 26 : 12, new Color(0.12f, 0.12f, 0.14f), TextAnchor.MiddleCenter);
                    num.rectTransform.sizeDelta = full ? new Vector2(60, 34) : new Vector2(28, 16);
                    num.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(-a), Mathf.Cos(a)) * dial * 0.36f;
                    num.rectTransform.localEulerAngles = new Vector3(0, 0, -n * 3.6f);
                    num.fontStyle = FontStyle.Bold;
                }
            }

            var knob = Img(panel.transform, "Knob", circle, new Color(0.13f, 0.13f, 0.15f));
            knob.rectTransform.sizeDelta = Vector2.one * dial * 0.36f;
            knob.rectTransform.anchoredPosition = dialCenter;

            var pointer = Img(panel.transform, "Pointer", tri, new Color32(0xE0, 0x48, 0x48, 0xFF));
            pointer.rectTransform.sizeDelta = full ? new Vector2(30, 22) : new Vector2(18, 13);
            pointer.rectTransform.anchoredPosition = dialCenter + new Vector2(0, dial * 0.5f + (full ? 6 : 4));

            var tickBox = Img(panel.transform, "TickBox", null, new Color(0.05f, 0.05f, 0.06f));
            tickBox.rectTransform.sizeDelta = full ? new Vector2(70, 40) : new Vector2(42, 24);
            tickBox.rectTransform.anchoredPosition = pointer.rectTransform.anchoredPosition + new Vector2(0, full ? 34 : 20);
            view.tickText = Txt(tickBox.transform, "Tick", "00", full ? 28 : 17, new Color32(0xFF, 0xD3, 0x4D, 0xFF), TextAnchor.MiddleCenter);
            Stretch(view.tickText.rectTransform);
            view.tickText.fontStyle = FontStyle.Bold;

            // 돌릴 방향
            Vector2 dirPos = full ? new Vector2(-400, 90) : new Vector2(30, -66);
            if (full)
            {
                Txt(panel.transform, "DirLabel", "돌릴 방향", 22, Muted, TextAnchor.MiddleCenter).rectTransform.anchoredPosition = dirPos + new Vector2(0, 70);
                var dirBg = Img(panel.transform, "DirBg", circle, new Color(0.06f, 0.06f, 0.07f));
                dirBg.rectTransform.sizeDelta = Vector2.one * 110;
                dirBg.rectTransform.anchoredPosition = dirPos;
                view.directionIcon = Txt(panel.transform, "DirIcon", "↻", 64, Color.white, TextAnchor.MiddleCenter);
                view.directionIcon.rectTransform.anchoredPosition = dirPos;
                view.directionIcon.rectTransform.sizeDelta = new Vector2(110, 110);
                view.directionText = Txt(panel.transform, "DirText", "오른쪽", 28, Color.white, TextAnchor.MiddleCenter);
                view.directionText.rectTransform.anchoredPosition = dirPos + new Vector2(0, -80);
                view.directionText.fontStyle = FontStyle.Bold;
            }
            else
            {
                // 작은 다이얼은 방향을 가운데 손잡이 위 화살표로만 표시
                view.directionIcon = Txt(panel.transform, "DirIcon", "↻", 30, Color.white, TextAnchor.MiddleCenter);
                view.directionIcon.rectTransform.sizeDelta = new Vector2(50, 50);
                view.directionIcon.rectTransform.anchoredPosition = dialCenter;
            }

            // 단계 램프
            Vector2 lampStart = full ? new Vector2(400, 90) : new Vector2(85, 28);
            float lampGap = full ? 62 : 34;
            float lampSize = full ? 38 : 24;
            if (full) Txt(panel.transform, "LampLabel", "단계", 22, Muted, TextAnchor.MiddleCenter).rectTransform.anchoredPosition = lampStart + new Vector2(0, 60);
            for (int i = 0; i < 3; i++)
            {
                var lamp = Img(panel.transform, "Lamp" + (i + 1), circle, SafeDialViewBase.LampOff);
                lamp.rectTransform.sizeDelta = Vector2.one * lampSize;
                lamp.rectTransform.anchoredPosition = lampStart + new Vector2(0, -i * lampGap);
                view.lamps[i] = lamp;
                if (!full) continue; // 작은 다이얼은 램프 번호 없음
                var label = Txt(panel.transform, "LampNum" + (i + 1), (i + 1).ToString(), 22, Muted, TextAnchor.MiddleCenter);
                label.rectTransform.sizeDelta = new Vector2(30, 30);
                label.rectTransform.anchoredPosition = lampStart + new Vector2(45, -i * lampGap);
            }

            // 조작 안내 (전체 창만)
            if (full)
            {
                var helpText = Txt(panel.transform, "Help", "A/D 돌리기 · Space 확정 · ESC 닫기 · 마우스로 좌우 드래그", 22, Muted, TextAnchor.MiddleCenter);
                helpText.rectTransform.sizeDelta = new Vector2(panelSize.x - 20, 30);
                helpText.rectTransform.anchoredPosition = new Vector2(0, -290);
            }

            // 닫기 버튼 (전체 창만)
            if (full)
            {
                var close = Img(panel.transform, "Close", null, new Color(0.08f, 0.08f, 0.1f));
                close.rectTransform.sizeDelta = new Vector2(52, 52);
                close.rectTransform.anchoredPosition = new Vector2(panelSize.x / 2 - 50, panelSize.y / 2 - 50);
                close.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, 0.5f);
                view.closeButton = close.gameObject.AddComponent<Button>();
                var x = Txt(close.transform, "X", "✕", 26, Color.white, TextAnchor.MiddleCenter);
                Stretch(x.rectTransform);
            }

            // 성공 표시
            // 전체 창은 창 전체를 덮고, 작은 다이얼은 다이얼 위에 둥글게 덮는다
            var success = Img(panel.transform, "Success", full ? null : circle, new Color(0, 0, 0, 0.88f));
            if (full) Stretch(success.rectTransform);
            else
            {
                success.rectTransform.sizeDelta = Vector2.one * (dial + 10);
                success.rectTransform.anchoredPosition = dialCenter;
            }
            float successWidth = full ? panelSize.x : dial + 60;
            var title = Txt(success.transform, "Title", "금고 열림", full ? 46 : 18, Color.white, TextAnchor.MiddleCenter);
            title.rectTransform.sizeDelta = new Vector2(successWidth, full ? 70 : 26);
            title.rectTransform.anchoredPosition = new Vector2(0, full ? 70 : 24);
            title.fontStyle = FontStyle.Bold;
            view.successMoney = Txt(success.transform, "Money", "+$0", full ? 90 : 30, new Color32(0xFF, 0xD3, 0x4D, 0xFF), TextAnchor.MiddleCenter);
            view.successMoney.rectTransform.sizeDelta = new Vector2(successWidth, full ? 120 : 44);
            view.successMoney.rectTransform.anchoredPosition = new Vector2(0, full ? -30 : -10);
            view.successMoney.fontStyle = FontStyle.Bold;
            view.successRoot = success.gameObject;
            success.gameObject.SetActive(false);

            if (!full)
            {
                // 작은 다이얼은 화면 왼쪽 아래 기준 좌표로 따라다닌다
                var rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
            }
            // 마우스 드래그·클릭은 다이얼 조작이므로 UI 가 가로채지 않게 한다 (X 버튼만 예외)
            foreach (var g in root.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = view.closeButton && g.gameObject == view.closeButton.gameObject;

            root.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static Text Txt(Transform parent, string name, string text, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.rectTransform.sizeDelta = new Vector2(200, 40);
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ---------------- 임시 그림 ----------------

        static Sprite SaveSprite(string name, Texture2D tex)
        {
            string path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // 숫자판: 밝은 원판 + 어두운 테두리 + 눈금 100개(10단위 길게)
        static Texture2D DrawFace(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = size / 2f, r = size / 2f - 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - d + 0.5f);
                    // 위쪽(0)에서 시계 방향 각도
                    float ang = (Mathf.Atan2(dx, dy) * Mathf.Rad2Deg + 360f) % 360f;
                    float tickPos = ang / 3.6f;
                    int nearest = Mathf.RoundToInt(tickPos) % 100;
                    float off = Mathf.Abs(tickPos - Mathf.Round(tickPos)) * 3.6f * Mathf.Deg2Rad * d; // 픽셀 단위 거리
                    bool major = nearest % 10 == 0, mid = nearest % 5 == 0;
                    float inner = major ? r * 0.80f : mid ? r * 0.85f : r * 0.88f;
                    float width = major ? 2.6f : 1.4f;
                    Color col;
                    if (d > r * 0.95f) col = new Color(0.16f, 0.16f, 0.18f);          // 테두리
                    else if (d > inner && off < width) col = new Color(0.1f, 0.1f, 0.12f); // 눈금
                    else
                    {
                        float shade = Mathf.Lerp(0.97f, 0.78f, d / r);                  // 금속 느낌 그라데이션
                        col = new Color(shade, shade, shade * 1.02f);
                    }
                    col.a = alpha;
                    px[y * size + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // thickness 0 이면 꽉 찬 원, 0.9 면 바깥 10% 만 있는 고리
        static Texture2D DrawCircle(int size, float hole)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = size / 2f, r = size / 2f - 1f, rIn = r * hole;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - c, dy = y + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (hole > 0f) a *= Mathf.Clamp01(d - rIn + 0.5f);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // 아래를 가리키는 삼각형
        static Texture2D DrawTriangle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float halfW = (y / (float)size) * size / 2f; // 위가 넓고 아래가 뾰족
                    float a = Mathf.Clamp01(halfW - Mathf.Abs(x + 0.5f - size / 2f) + 0.5f);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
