// GPL-3.0-only. Independently implemented Win32 COM bridge; no SharpShell or CLR.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <atomic>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <map>
#include <set>
#include <string>
#include <vector>
#include <stdexcept>
#include <cstring>

static const CLSID ClassId = {0x61699489,0x9eea,0x43d6,{0xbf,0x5b,0x32,0xdb,0x32,0xb7,0xa2,0x02}};
static std::atomic<long> live{0}, locks{0};
static HMODULE module;
static std::wstring widen(const std::string& input) {
    if(input.empty()) return {};
    int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,input.data(),(int)input.size(),nullptr,0);
    if(!n) throw std::runtime_error("utf8");
    std::wstring value(n,L'\0');
    MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,input.data(),(int)input.size(),value.data(),n);
    return value;
}
static std::string utf8(const std::wstring& input) {
    int n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,input.data(),(int)input.size(),nullptr,0,nullptr,nullptr);
    if(!n && !input.empty()) throw std::runtime_error("unicode");
    std::string value(n,'\0');
    if(n) WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,input.data(),(int)input.size(),value.data(),n,nullptr,nullptr);
    return value;
}
static std::filesystem::path dataDirectory() {
    wchar_t fixture[32768];auto length=GetEnvironmentVariableW(L"FILECONVERTER_SHELL_TEST_ROOT",fixture,32768);
    if(length>0&&length<32768)return std::filesystem::absolute(fixture);
    PWSTR path=nullptr;
    if(FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData,0,nullptr,&path))) throw std::runtime_error("appdata");
    std::filesystem::path value(path); CoTaskMemFree(path); return value/L"FileConverter";
}
struct Preset { std::wstring id,name; std::vector<std::wstring> inputs; };
struct Snapshot { std::wstring executable,settings; bool icons=false; std::vector<Preset> presets; };
struct Reader {
    std::vector<char> bytes; size_t offset=0;
    int count(int max) {
        if(offset+4>bytes.size()) throw std::runtime_error("truncated");
        int n; memcpy(&n,bytes.data()+offset,4); offset+=4;
        if(n<0 || n>max) throw std::runtime_error("bounds"); return n;
    }
    std::wstring text() {
        auto n=count(65536); if(offset+n>bytes.size()) throw std::runtime_error("truncated");
        auto text=widen(std::string(bytes.data()+offset,n)); offset+=n;
        if(text.find(L'\0')!=text.npos) throw std::runtime_error("nul"); return text;
    }
};
static Snapshot readSnapshot() {
    std::ifstream file(dataDirectory()/L"menu.bin",std::ios::binary|std::ios::ate);
    if(!file) throw std::runtime_error("snapshot");
    auto n=file.tellg(); if(n<4 || n>4*1024*1024) throw std::runtime_error("size");
    Reader r; r.bytes.resize((size_t)n); file.seekg(0); file.read(r.bytes.data(),n);
    if(!file || memcmp(r.bytes.data(),"FCM1",4)) throw std::runtime_error("version");
    r.offset=4; Snapshot s; s.executable=r.text(); s.settings=r.text(); s.icons=r.count(1)!=0;
    int presets=r.count(2048);
    for(int i=0;i<presets;i++) {
        Preset p; p.id=r.text(); p.name=r.text(); auto inputs=r.count(256);
        for(int j=0;j<inputs;j++) p.inputs.push_back(r.text());
        if(p.id.empty()||p.name.empty()||p.name.size()>1024||std::count(p.name.begin(),p.name.end(),L'/')>15)throw std::runtime_error("preset bounds");
        s.presets.push_back(std::move(p));
    }
    if(r.offset!=r.bytes.size()) throw std::runtime_error("trailing"); return s;
}
static std::wstring escapeLabel(const std::wstring& text) {
    std::wstring result; for(auto c:text) {result+=c; if(c==L'&') result+=c;} return result;
}
static std::string jsonText(const std::wstring& text) {
    auto bytes=utf8(text); std::string result="\"";
    for(unsigned char c:bytes) {
        if(c=='"' || c=='\\') {result+='\\';result+=(char)c;}
        else if(c<32) {char buffer[7];sprintf_s(buffer,"\\u%04x",(unsigned)c);result+=buffer;}
        else result+=(char)c;
    }
    return result+"\"";
}
static std::wstring quote(const std::wstring& text) {
    std::wstring result=L"\""; size_t slashes=0;
    for(auto c:text) {
        if(c==L'\\') {slashes++;continue;}
        if(c==L'"') {result.append(slashes*2+1,L'\\');result+=c;}
        else {result.append(slashes,L'\\');result+=c;} slashes=0;
    }
    result.append(slashes*2,L'\\');return result+L"\"";
}
static void launch(const Snapshot& s,const Preset* preset,const std::vector<std::wstring>& files) {
    std::filesystem::path request;
    auto args=quote(s.executable);
    if(!preset) args+=L" --settings";
    else {
        auto dir=dataDirectory()/L"Requests";std::filesystem::create_directories(dir);
        GUID id; if(FAILED(CoCreateGuid(&id))) throw std::runtime_error("guid");
        wchar_t guid[40];StringFromGUID2(id,guid,40);
        request=dir/(std::wstring(guid)+L".json");
        std::string json="{\"Version\":1,\"Preset\":"+jsonText(preset->id)+",\"Files\":[";
        for(size_t i=0;i<files.size();i++) {if(i)json+=",";json+=jsonText(files[i]);}
        json+="]}";
        HANDLE file=CreateFileW(request.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file==INVALID_HANDLE_VALUE) throw std::runtime_error("request");
        DWORD written=0;bool ok=WriteFile(file,json.data(),(DWORD)json.size(),&written,nullptr)&&written==json.size()&&FlushFileBuffers(file);
        CloseHandle(file);if(!ok) {DeleteFileW(request.c_str());throw std::runtime_error("write");}
        args+=L" --request "+quote(request.wstring());
    }
    STARTUPINFOW startup{sizeof(startup)};PROCESS_INFORMATION process{};
    if(!CreateProcessW(s.executable.c_str(),args.data(),nullptr,nullptr,FALSE,0,nullptr,nullptr,&startup,&process)) {
        if(!request.empty())DeleteFileW(request.c_str());throw std::runtime_error("launch");
    }
    CloseHandle(process.hThread);CloseHandle(process.hProcess);
}
class Menu final:public IShellExtInit,public IContextMenu {
    std::atomic<ULONG> refs{1};
    Snapshot snapshot; std::vector<std::wstring> files;
    struct Command {int preset;bool enabled;};
    std::vector<Command> commands;
    HBITMAP presetBitmap=nullptr,folderBitmap=nullptr;
    HBITMAP bitmap(bool folder) {
        auto& cached=folder?folderBitmap:presetBitmap;
        if(!cached){
            wchar_t dllPath[32768];auto n=GetModuleFileNameW(module,dllPath,32768);
            if(!n||n>=32768)return nullptr;
            auto path=std::filesystem::path(dllPath).parent_path()/(folder?L"folder.bmp":L"preset.bmp");
            cached=(HBITMAP)LoadImageW(nullptr,path.c_str(),IMAGE_BITMAP,16,16,LR_LOADFROMFILE|LR_CREATEDIBSECTION);
        }
        return cached;
    }
    void icon(HMENU menu,UINT item,bool position,bool folder){
        auto image=bitmap(folder);if(!image)return;
        MENUITEMINFOW info{sizeof(info)};info.fMask=MIIM_BITMAP;info.hbmpItem=image;SetMenuItemInfoW(menu,item,position,&info);
    }
public:
    Menu(){live++;} ~Menu(){if(presetBitmap)DeleteObject(presetBitmap);if(folderBitmap)DeleteObject(folderBitmap);live--;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown || iid==IID_IShellExtInit)*result=static_cast<IShellExtInit*>(this);
        else if(iid==IID_IContextMenu)*result=static_cast<IContextMenu*>(this);else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{auto n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE,IDataObject* data,HKEY) override {
        try {
            files.clear();commands.clear();
            if(!data)return E_INVALIDARG;
            FORMATETC format{CF_HDROP,nullptr,DVASPECT_CONTENT,-1,TYMED_HGLOBAL};STGMEDIUM medium{};
            auto hr=data->GetData(&format,&medium);if(FAILED(hr))return hr;
            struct Guard{STGMEDIUM* p;~Guard(){ReleaseStgMedium(p);}} guard{&medium};
            auto drop=(HDROP)medium.hGlobal;
            auto count=DragQueryFileW(drop,UINT_MAX,nullptr,0);if(count>100000)return E_INVALIDARG;
            for(UINT i=0;i<count;i++) {
                auto n=DragQueryFileW(drop,i,nullptr,0);if(!n||n>32767)continue;
                std::wstring path(n+1,L'\0');DragQueryFileW(drop,i,path.data(),n+1);path.resize(n);files.push_back(std::move(path));
            }
            snapshot=readSnapshot();return S_OK;
        }catch(...){return E_FAIL;}
    }
    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU parent,UINT position,UINT first,UINT last,UINT flags) override {
        HMENU root=nullptr;
        try {
            commands.clear();if(flags&CMF_DEFAULTONLY)return S_OK;
            std::set<std::wstring> extensions;
            for(const auto& file:files) {
                auto ext=std::filesystem::path(file).extension().wstring();
                if(!ext.empty())ext.erase(0,1);
                std::transform(ext.begin(),ext.end(),ext.begin(),[](wchar_t c){return (wchar_t)towlower(c);});extensions.insert(ext);
            }
            if(extensions.empty())return S_OK;
            for(size_t i=0;i<snapshot.presets.size();i++) {
                auto& preset=snapshot.presets[i];size_t compatible=0;
                for(const auto& ext:extensions)if(std::find(preset.inputs.begin(),preset.inputs.end(),ext)!=preset.inputs.end())compatible++;
                if(compatible)commands.push_back({(int)i,compatible==extensions.size()});
            }
            if(commands.empty())return S_OK;
            if(last<first||commands.size()+1>(uint64_t)last-first+1){commands.clear();return S_OK;}
            root=CreatePopupMenu();if(!root)throw std::runtime_error("menu");
            std::map<std::wstring,HMENU> folders;folders[L""]=root;
            for(size_t index=0;index<commands.size();index++) {
                const auto& command=commands[index];auto name=snapshot.presets[command.preset].name;
                std::replace(name.begin(),name.end(),L'\\',L'/');size_t start=0,pos;std::wstring path;HMENU menu=root;
                while((pos=name.find(L'/',start))!=name.npos) {
                    auto part=name.substr(start,pos-start);if(!path.empty())path+=L"/";path+=part;
                    if(!folders.count(path)) {
                        HMENU child=CreatePopupMenu();if(!child)throw std::runtime_error("submenu");
                        if(!AppendMenuW(menu,MF_POPUP,(UINT_PTR)child,escapeLabel(part).c_str())){DestroyMenu(child);throw std::runtime_error("append");}
                        if(snapshot.icons)icon(menu,(UINT)GetMenuItemCount(menu)-1,true,true);
                        folders[path]=child;
                    }
                    menu=folders[path];start=pos+1;
                }
                if(!AppendMenuW(menu,MF_STRING|(command.enabled?MF_ENABLED:MF_GRAYED),first+index,escapeLabel(name.substr(start)).c_str()))throw std::runtime_error("preset");
                if(snapshot.icons)icon(menu,(UINT)(first+index),false,false);
            }
            AppendMenuW(root,MF_SEPARATOR,0,nullptr);
            if(!AppendMenuW(root,MF_STRING,first+commands.size(),escapeLabel(snapshot.settings).c_str()))throw std::runtime_error("settings");
            commands.push_back({-1,true});
            if(!InsertMenuW(parent,position,MF_BYPOSITION|MF_POPUP,(UINT_PTR)root,L"File Converter"))throw std::runtime_error("insert");
            root=nullptr;return MAKE_HRESULT(SEVERITY_SUCCESS,0,(USHORT)commands.size());
        }catch(...){commands.clear();if(root)DestroyMenu(root);return E_FAIL;}
    }
    std::wstring canonical(size_t index) {
        return commands[index].preset<0?L"fileconverter.settings":L"fileconverter."+snapshot.presets[commands[index].preset].id;
    }
    HRESULT STDMETHODCALLTYPE InvokeCommand(CMINVOKECOMMANDINFO* info) override {
        try {
            if(!info||info->cbSize<sizeof(CMINVOKECOMMANDINFO))return E_INVALIDARG;
            bool unicode=(info->fMask&CMIC_MASK_UNICODE)&&info->cbSize>=sizeof(CMINVOKECOMMANDINFOEX);
            size_t index=commands.size();
            auto wideVerb=unicode?((CMINVOKECOMMANDINFOEX*)info)->lpVerbW:nullptr;
            // Unicode strings use lpVerbW; numeric offsets use lpVerb even with the Unicode flag.
            if(unicode&&(UINT_PTR)wideVerb>0xffff){
                for(size_t i=0;i<commands.size();i++)if(_wcsicmp(canonical(i).c_str(),wideVerb)==0){index=i;break;}
            }
            else if((UINT_PTR)info->lpVerb<=0xffff)index=(UINT_PTR)info->lpVerb;
            else {
                if(unicode)return E_INVALIDARG;
                std::wstring text;
                auto n=MultiByteToWideChar(CP_ACP,0,info->lpVerb,-1,nullptr,0);if(!n)return E_INVALIDARG;text.resize(n);MultiByteToWideChar(CP_ACP,0,info->lpVerb,-1,text.data(),n);text.resize(n-1);
                for(size_t i=0;i<commands.size();i++)if(_wcsicmp(canonical(i).c_str(),text.c_str())==0){index=i;break;}
            }
            if(index>=commands.size()||!commands[index].enabled)return E_INVALIDARG;
            auto n=commands[index].preset;launch(snapshot,n<0?nullptr:&snapshot.presets[n],files);return S_OK;
        }catch(...){return E_FAIL;}
    }
    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR id,UINT type,UINT*,LPSTR buffer,UINT count) override {
        try {
            if(id>=commands.size())return E_INVALIDARG;if(type==GCS_VALIDATEA||type==GCS_VALIDATEW)return S_OK;
            if(!buffer||!count)return E_INVALIDARG;
            auto value=type==GCS_VERBA||type==GCS_VERBW?canonical(id):commands[id].preset<0?L"File Converter":snapshot.presets[commands[id].preset].name;
            if(type&GCS_UNICODE){auto n=std::min(value.size(),(size_t)count-1);memcpy(buffer,value.data(),n*sizeof(wchar_t));((wchar_t*)buffer)[n]=0;}
            else {
                auto length=(int)value.size();
                while(length>0&&(UINT)WideCharToMultiByte(CP_ACP,0,value.data(),length,nullptr,0,nullptr,nullptr)>count-1)length--;
                auto n=length==0?0:WideCharToMultiByte(CP_ACP,0,value.data(),length,buffer,(int)count-1,nullptr,nullptr);buffer[n]=0;
            }
            return S_OK;
        }catch(...){return E_FAIL;}
    }
};
class Factory final:public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    Factory(){live++;}~Factory(){live--;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(id!=IID_IUnknown&&id!=IID_IClassFactory)return E_NOINTERFACE;*result=this;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override{auto n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID id,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(outer)return CLASS_E_NOAGGREGATION;
        try{auto menu=new Menu();auto hr=menu->QueryInterface(id,result);menu->Release();return hr;}catch(...){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL value) override{if(value)locks++;else if(locks>0)locks--;return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID id,REFIID iid,void** result) {
    if(!result)return E_POINTER;*result=nullptr;if(id!=ClassId)return CLASS_E_CLASSNOTAVAILABLE;
    try{auto factory=new Factory();auto hr=factory->QueryInterface(iid,result);factory->Release();return hr;}catch(...){return E_OUTOFMEMORY;}
}
STDAPI DllCanUnloadNow(){return live==0&&locks==0?S_OK:S_FALSE;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
