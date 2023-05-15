#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Augmentations.SkyboxCreator
{

    public class SkyboxCreator : EditorWindow
    {
        private bool _isRunning = false;

        private bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                if (value)
                {
                    _referenceTextField.SetEnabled(false);
                    _unityDirTextField.SetEnabled(false);
                    _browseBtn1.SetEnabled(false);
                    _browseBtn2.SetEnabled(false);
                    _startBtn.SetEnabled(false);
                    rootVisualElement.Q<Foldout>()?.SetEnabled(false);
                    _abortBtn.SetEnabled(true);
                }
                else
                {
                    _referenceTextField.SetEnabled(true);
                    _unityDirTextField.SetEnabled(true);
                    _browseBtn1.SetEnabled(true);
                    _browseBtn2.SetEnabled(true);
                    _startBtn.SetEnabled(true);
                    _abortBtn.SetEnabled(false);
                    rootVisualElement.Q<Foldout>()?.SetEnabled(true);
                    _statusLabel.text = string.Empty;
                }
            }
        }

        private string RefFile => _referenceTextField.value;
        private string UnityDir => _unityDirTextField.value;

        private string RelativeUnityDir =>
            UnityDir.StartsWith(Application.dataPath)
                ? "Assets" + UnityDir.Substring(Application.dataPath.Length)
                : UnityDir;

        private string RelativeHdriDir => Path.Combine(RelativeUnityDir, "HDRIs");
        private string RelativeSkyboxDir => Path.Combine(RelativeUnityDir, "Skyboxes");

        private readonly HttpClient _httpClient = new HttpClient();

        private bool _isAutoRefreshEnabled;

        private TextField _referenceTextField;
        private TextField _unityDirTextField;
        private Button _browseBtn1;
        private Button _browseBtn2;
        private Button _startBtn;
        private Button _abortBtn;
        private Toggle _hdriOverwriteToggle;
        private Toggle _skyboxOverwriteToggle;
        private Label _statusLabel;

        [MenuItem("Tools/SkyboxCreator")]
        public static void ShowWindow()
        {
            var wnd = GetWindow<SkyboxCreator>();
            wnd.titleContent = new GUIContent("SkyboxCreator");
        }

        private void OnEnable()
        {
            _isAutoRefreshEnabled = EditorPrefs.GetBool("AutoRefresh");
            if (_isAutoRefreshEnabled)
                AssetDatabase.DisallowAutoRefresh();
        }

        private void OnDestroy()
        {
            IsRunning = false;
            if (_isAutoRefreshEnabled)
                AssetDatabase.AllowAutoRefresh();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/SkyboxCreator/SkyboxCreator.uxml");
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/SkyboxCreator/SkyboxCreator.uss");

            VisualElement uxmlRoot = visualTree.Instantiate();
            uxmlRoot.styleSheets.Add(styleSheet);
            root.Add(uxmlRoot);

            _referenceTextField = uxmlRoot.Q<TextField>("referenceTextField");
            _unityDirTextField = uxmlRoot.Q<TextField>("unityDirTextField");
            _browseBtn1 = root.Q<Button>("browseBtn1");
            _browseBtn2 = root.Q<Button>("browseBtn2");
            _startBtn = root.Q<Button>("startBtn");
            _abortBtn = root.Q<Button>("abortBtn");
            _hdriOverwriteToggle = root.Q<Toggle>("hdriOverwriteToggle");
            _skyboxOverwriteToggle = root.Q<Toggle>("skyboxOverwriteToggle");
            _statusLabel = root.Q<Label>("statusLabel");

            _browseBtn1.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                _referenceTextField.value = EditorUtility.OpenFilePanel(
                    title: "please select the .txt file containing the URIs of HDRI files",
                    directory: Application.dataPath,
                    extension: "txt");
            });

            _browseBtn2.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                _unityDirTextField.value = EditorUtility.OpenFolderPanel(
                    title: "please select a project dir to be used for processing",
                    folder: Application.dataPath,
                    defaultName: string.Empty);
            });

            _abortBtn.RegisterCallback<ClickEvent>(evt => IsRunning = false);
            _startBtn.RegisterCallback<ClickEvent>(OnClickingStart);

            IsRunning = false;
        }

        private void OnClickingStart(ClickEvent evt)
        {
            evt.StopPropagation();
            _ = DoStart();
        }

        private async Task DoStart()
        {
            if (!File.Exists(RefFile))
            {
                EditorUtility.DisplayDialog("The specified file path is invalid",
                    $"The inputted path, \"{RefFile}\", does not exist!\n(Please make sure you input the path to a source .txt file correctly)",
                    "Okay");
                _referenceTextField.value = string.Empty;
                return;
            }

            if (!Directory.Exists(UnityDir))
            {
                EditorUtility.DisplayDialog("The inputted unity project directory does not exist",
                    $"The inputted unity project directory, \"{UnityDir}\", does not exist!\n(Please reenter a valid directory as output directory)",
                    "Okay");
                _unityDirTextField.value = string.Empty;
                return;
            }

            IsRunning = true;

            Directory.CreateDirectory(RelativeHdriDir);
            Directory.CreateDirectory(RelativeSkyboxDir);

            await FetchFiles();
            await CreateCubemaps();
            await CreateSkyboxes();

            _statusLabel.text = "All is done ＼(^o^)／";
            _abortBtn.text = "Close Me";
            _abortBtn.RegisterCallback<ClickEvent>(evt => Close());
            _startBtn.text = ":)";
        }

        private async Task FetchFiles()
        {
            var urls = File.ReadLines(RefFile, Encoding.UTF8);
            var urlsAsArray = urls.ToArray();
            if (urlsAsArray.Length == 0)
                IsRunning = false;


            var absoluteHdriDirPath = Path.GetFullPath(RelativeHdriDir);
            var highValue = urlsAsArray.Length;
            var currentValue = 0;

            foreach (var url in urlsAsArray)
            {
                if (!IsRunning) return;
                var fileName = Path.GetFileName(url);
                var aRelativeFilePath = Path.Combine(absoluteHdriDirPath, fileName);

                _statusLabel.text = $"downloading {fileName}, {currentValue + 1} / {highValue}";
                currentValue++;

                if ((_hdriOverwriteToggle.value == false)
                    & File.Exists(aRelativeFilePath))
                    continue;

                try
                {
                    var response = await _httpClient.GetByteArrayAsync(url);
                    File.WriteAllBytes(aRelativeFilePath, response);

                }
                catch (HttpRequestException e)
                {
                    Debug.Log(e.Message);
                }
            }
            AssetDatabase.Refresh();
        }

        private Task CreateCubemaps()
        {
            var hdriArray = Directory.GetFiles(RelativeHdriDir, "*.exr");
            if (hdriArray.Length == 0)
                IsRunning = false;

            foreach (var hdriFilePath in hdriArray)
            {
                if (!IsRunning) return Task.CompletedTask;

                var fileName = Path.GetFileNameWithoutExtension(hdriFilePath);

                var aTexture = (TextureImporter)AssetImporter.GetAtPath(hdriFilePath);
                aTexture.name = fileName;
                aTexture.GetDefaultPlatformTextureSettings();
                aTexture.textureShape = TextureImporterShape.TextureCube;
                aTexture.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                aTexture.wrapMode = TextureWrapMode.Clamp;
                aTexture.maxTextureSize = 4096;
                aTexture.alphaSource = TextureImporterAlphaSource.None;
                aTexture.SaveAndReimport();
            }
            return Task.CompletedTask;
        }

        private Task CreateSkyboxes()
        {
            var hdriArray = Directory.GetFiles(RelativeHdriDir, "*.exr");
            if (hdriArray.Length == 0)
                IsRunning = false;

            foreach (var hdriFilePath in hdriArray)
            {
                if (!IsRunning) return Task.CompletedTask;

                var fileName = Path.GetFileNameWithoutExtension(hdriFilePath);

                var aRelativeFilePath = Path.Combine(RelativeSkyboxDir, fileName);

                var aMaterial = new Material(Shader.Find("Skybox/Cubemap"));
                var aCubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(hdriFilePath);
                aCubemap.wrapMode = TextureWrapMode.Clamp;
                aCubemap.name = fileName;
                aMaterial.SetTexture("_Tex", aCubemap);
                if (_skyboxOverwriteToggle.value == false
                    & File.Exists(aRelativeFilePath + ".mat"))
                    continue;
                AssetDatabase.CreateAsset(aMaterial, aRelativeFilePath + ".mat");
            }
            AssetDatabase.Refresh();

            return Task.CompletedTask;
        }
    }
}
#endif
