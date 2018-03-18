#r "$NuGet\System.IO.Compression.ZipFile\4.3.0\lib\net46\System.IO.Compression.ZipFile.dll"
#r "System.IO.Compression.FileSystem"
/*
иcходник можно запустить через:
* roslynpad - https://roslynpad.net 
* "c:\Program Files (x86)\MSBuild\14.0\Bin\csi.exe"
* visual studio (с небольшими поправкими кода)

требуется ffmpeg в той же директории. его можно скачать тут - https://www.ffmpeg.org/download.html#build-windows
*/


#r "$NuGet\Newtonsoft.Json\11.0.1\lib\net45\Newtonsoft.Json.dll"
using Newtonsoft.Json;
using System.Net;
//using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.IO.Compression;

#pragma warning disable 0649
class Region
{
    public int region_id;
    public List<VotingStation> voting_stations;
}

class VotingStation
{
    public int id { get; set; }
    public string rid;
    public string kind;
    public int number;
    public int people;
    public string address;
    public int region_id;
    public string region_name;
    public int utc_offset;
    public bool is_active;
    public bool is_standalone;
    public decimal latitude;
    public decimal longitude;
    public int broadcast_state;
    //public DateTime broadcast_state_updated_at":"2018-03-17T18:10:26.077532",
    public string full_address { get; set; }

}

class Camera
{
    public int voting_station_id;
    public string kind;
    public string uuid;
    public string[] streamers_hls;
    public string view;
    public int camera_number;
    public string name;
    public int region_id;
    public string video_bitrate;
    public bool is_alive;
    public bool video_enabled;
    public bool audio_enabled;
    public string session_id;
    public string demand_token;
}

#pragma warning restore 0649

List<VotingStation> ShowFilterDialog(List<VotingStation> stations)
{
    Form prompt = new Form()
    {
        Width = 500,
        Height = 500,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        Text = "select stations",
        StartPosition = FormStartPosition.CenterScreen
    };
    Label textLabel = new Label() { Left = 50, Top = 20, Width = 400, Text = "filter by addr:" };
    TextBox textBox = new TextBox() { Left = 50, Top = 50, Width = 400 };
    Button confirmation = new Button() { Text = "Ok", Left = 350, Width = 100, Top = 70, DialogResult = DialogResult.OK };
    ListBox listBox = new ListBox() { Left = 50, Top = 100, Width = 400, Height = 300 };
    listBox.DataSource = stations;
    listBox.DisplayMember = "full_address";
    listBox.ValueMember = "id";
    listBox.SelectionMode = SelectionMode.MultiExtended;


    textBox.TextChanged += (sender, e) => { if (textBox.Text.Length > 3) listBox.DataSource = stations.Where(x => Regex.IsMatch(x.full_address, textBox.Text.Replace(" ", ".+"))).ToList(); };

    confirmation.Click += (sender, e) =>
    {
        var res = listBox.SelectedItems.OfType<VotingStation>().ToList();
        if (res.Count == 0) throw new Exception("select some");
        if (res.Count > 10) throw new Exception("sorry, too many stations! you dont want them");
        prompt.Close();
    };
    prompt.Controls.Add(textBox);
    prompt.Controls.Add(confirmation);
    prompt.Controls.Add(textLabel);
    prompt.Controls.Add(listBox);
    prompt.AcceptButton = confirmation;
    return prompt.ShowDialog() == DialogResult.OK ? listBox.SelectedItems.OfType<VotingStation>().ToList() : new List<VotingStation>();
}




List<VotingStation> GetStationsToCaptureFromFile()
{
    // list from http://www.nashvybor2018.ru/api/voting_stations.json
    using (ZipArchive zip = ZipFile.OpenRead("voting_stations.json.zip"))
    {
        var file = zip.Entries.Single(f => f.Name == "voting_stations.json");

        List<VotingStation> allstations;
        using (var rdr = new StreamReader(file.Open(), Encoding.UTF8))
            allstations = new JsonSerializer().Deserialize<List<Region>>(new JsonTextReader(rdr)).SelectMany(x => x.voting_stations).ToList();
        var res = ShowFilterDialog(allstations);
        if (res.Count == 0) throw new Exception("select some");
        if (res.Count > 10) throw new Exception("sorry, too many stations! you dont want them");
        return res;
    }
}


Directory.CreateDirectory("video-output");
string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
WebClient web = new WebClient();
web.Headers.Set("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.186 Safari/537.36 OPR/51.0.2830.55");

List<string> batch = new List<string>();
try
{
    foreach (VotingStation station in GetStationsToCaptureFromFile())
    {
        List<Camera> cams = JsonConvert.DeserializeObject<List<Camera>>(web.DownloadString($"http://www.nashvybor2018.ru/api/channels/{station.region_id}/{station.id}.json"));
        foreach (Camera cam in cams)
        {
            string camID = $"station{cam.voting_station_id}-cam{cam.camera_number}";
            string magicID = "a00eac6d137e156e376c2a5e7d488dd2";

            string m3u = web.DownloadString($"http://{cam.streamers_hls.First()}/{magicID}.m3u8?sid={cam.uuid}&session_id={cam.session_id}");
            string stream_url = m3u.Split('\n').Where(s => !s.StartsWith("#")).First(s => s.ToLower().StartsWith("http"));
            string ffmpegCmd = $"ffmpeg.exe -protocol_whitelist file,http,https,tcp,tls,crypto -i \"{stream_url}\" -c copy \"video-output\\{camID}\"-{timestamp}.mp4";
            batch.Add($"start " + ffmpegCmd);
            //Process.Start("cmd", " /C " + ffmpegCmd);
        }
    }
    File.WriteAllLines($"CAPTURE-{timestamp}.BAT", batch.ToArray());
    //Process.Start("cmd", $" /C CAPTURE-{timestamp}.BAT");
    MessageBox.Show($"please run CAPTURE-{timestamp}.BAT manually");
}
catch (Exception e)
{
    MessageBox.Show(e.Message, "ERROR");
}