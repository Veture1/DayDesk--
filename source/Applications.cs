using System;
using System.Collections.Generic;
using System.Linq;

namespace DayDesk {
public class JobApplication {
    public string Id { get; set; }
    public string Company { get; set; }
    public string Role { get; set; }
    public string Status { get; set; }
    public string AppliedDate { get; set; }
    public string Notes { get; set; }
}
public partial class DeskStore {
    public static readonly string[] ApplicationStatuses = {"planned","applied","assessment","interview","offer","rejected","watch","no_response"};
    public static readonly string[] ApplicationLabels = {"待投递","已投递","笔试 / OA","面试","Offer","未通过","观望","暂无回复"};
    public static string ApplicationLabel(string status) { int i=Array.IndexOf(ApplicationStatuses,status);return i<0?status:ApplicationLabels[i]; }
    public static string ApplicationSummary(Track track) {
        var items=track.Applications??new List<JobApplication>();
        return "待投 "+items.Count(x=>x.Status=="planned")+" · 已投 "+items.Count(x=>x.Status!="planned"&&x.Status!="watch")+" · 观望 "+items.Count(x=>x.Status=="watch")+" · 面试 "+items.Count(x=>x.Status=="interview");
    }
    static string Clean(string value,int max) { string s=(value??"").Trim();if(s.Length>max)throw new ArgumentException("填写内容过长。");return s; }
    static bool IsWebLink(string text) { Uri uri;return String.IsNullOrEmpty(text)||(text.Length<=2000&&Uri.TryCreate(text,UriKind.Absolute,out uri)&&(uri.Scheme=="https"||uri.Scheme=="http")); }
    static void ValidateApplications(Track track,HashSet<string> ids) {
        if(track.Applications==null||!IsWebLink(track.ApplicationsSourceUrl))throw new ArgumentException("投递清单或链接无效。");
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var a in track.Applications) {
            if(a==null||String.IsNullOrWhiteSpace(a.Company)||a.Company.Length>200||(a.Role??"").Length>300||!ApplicationStatuses.Contains(a.Status)||!ValidDate(a.AppliedDate,true)||(a.Notes??"").Length>10000)throw new ArgumentException("投递记录格式无效。");
            EnsureId(ids,a.Id);
            if(!keys.Add(a.Company.Trim()+"\n"+(a.Role??"").Trim()))throw new ArgumentException("同一主线已有该公司与岗位，请修改已有记录。");
        }
    }
    static JobApplication SaveApplicationIn(Track track,JobApplication value) {
        var existing=String.IsNullOrEmpty(value.Id)?null:track.Applications.FirstOrDefault(x=>x.Id==value.Id);
        if(!String.IsNullOrEmpty(value.Id)&&existing==null)throw new ArgumentException("未找到这条投递记录。");
        var a=new JobApplication {Id=existing==null?NewId():existing.Id,Company=Clean(value.Company,200),Role=Clean(value.Role,300),Status=value.Status,AppliedDate=Clean(value.AppliedDate,10),Notes=Clean(value.Notes,10000)};
        if(String.IsNullOrWhiteSpace(a.Company))throw new ArgumentException("请填写公司名称。");
        if(existing==null)track.Applications.Add(a);else track.Applications[track.Applications.IndexOf(existing)]=a;
        track.ApplicationsEnabled=true;
        return a;
    }
    public void SaveApplication(string trackId,JobApplication value) { CommitChange(s=>SaveApplicationIn(FindTrack(s,trackId),value)); }
    public void DeleteApplication(string trackId,string id) { CommitChange(s=>{var t=FindTrack(s,trackId);var a=t.Applications.FirstOrDefault(x=>x.Id==id);if(a==null)throw new ArgumentException("投递记录不存在。");t.Applications.Remove(a);}); }
    public void SetApplicationsSource(string trackId,string url) { CommitChange(s=>{var t=FindTrack(s,trackId);t.ApplicationsSourceUrl=Clean(url,2000);t.ApplicationsEnabled=true;}); }
    static void ApplyApplicationOperation(AppState state,Dictionary<string,object> op,string type,List<string> descriptions) {
        if(type=="set_applications_source") {
            CheckKeys(op,"type","trackId","url");var t=FindTrack(state,Required(op,"trackId",160));t.ApplicationsSourceUrl=Optional(op,"url","",2000);t.ApplicationsEnabled=true;
            descriptions.Add(t.Title+"：更新投递清单链接");return;
        }
        CheckKeys(op,"type","trackId","applicationId","company","role","status","appliedDate","notes");
        var track=FindTrack(state,Required(op,"trackId",160));
        string company=Required(op,"company",200),id=Optional(op,"applicationId","",160),role=Optional(op,"role","",300);
        var old=String.IsNullOrEmpty(id)?track.Applications.FirstOrDefault(x=>String.Equals(x.Company,company,StringComparison.OrdinalIgnoreCase)&&String.Equals(x.Role??"",role,StringComparison.OrdinalIgnoreCase)):track.Applications.FirstOrDefault(x=>x.Id==id);
        if(!String.IsNullOrEmpty(id)&&old==null)throw new ArgumentException("投递记录 ID 不存在。");
        var item=new JobApplication {Id=old==null?null:old.Id,Company=company,Role=Optional(op,"role",old==null?"":old.Role??"",300),Status=Required(op,"status",30),AppliedDate=DateValue(op,"appliedDate",old==null?"":old.AppliedDate,true),Notes=Optional(op,"notes",old==null?"":old.Notes??"",10000)};
        SaveApplicationIn(track,item);
        descriptions.Add(track.Title+" · "+item.Company+" → "+ApplicationLabel(item.Status));
    }
}
}
