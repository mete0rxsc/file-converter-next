#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <vector>
#include <string>
#include <stdexcept>
#include <atomic>

static void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
static std::filesystem::path appData(){
    wchar_t fixture[32768];auto length=GetEnvironmentVariableW(L"FILECONVERTER_SHELL_TEST_ROOT",fixture,32768);
    if(length>0&&length<32768)return std::filesystem::absolute(fixture);
    PWSTR text=nullptr;require(SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData,0,nullptr,&text)),"appdata");
    std::filesystem::path p(text);CoTaskMemFree(text);return p/L"FileConverter";
}
static void int32(std::ofstream& out,int n){out.write((char*)&n,4);}
static void text(std::ofstream& out,const std::wstring& value){
    auto n=WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),nullptr,0,nullptr,nullptr);
    std::string buffer(n,'\0');WideCharToMultiByte(CP_UTF8,0,value.data(),(int)value.size(),buffer.data(),n,nullptr,nullptr);
    int32(out,n);out.write(buffer.data(),n);
}
struct SnapshotGuard{
    std::filesystem::path file;std::vector<char> old;bool existed;
    SnapshotGuard():file(appData()/L"menu.bin"),existed(std::filesystem::exists(file)){
        if(existed){std::ifstream f(file,std::ios::binary);old=std::vector<char>(std::istreambuf_iterator<char>(f),{});}
        std::filesystem::create_directories(file.parent_path());
    }
    ~SnapshotGuard(){if(existed){std::ofstream f(file,std::ios::binary);f.write(old.data(),old.size());}else std::filesystem::remove(file);}
    void write(){
        wchar_t exe[32768];GetModuleFileNameW(nullptr,exe,32768);
        std::ofstream f(file,std::ios::binary);f.write("FCM1",4);text(f,exe);text(f,L"Configure");int32(f,1);int32(f,3);
        text(f,L"png");text(f,L"Images/\x56fe\x7247PNG");int32(f,1);text(f,L"png");
        text(f,L"webp");text(f,L"Images/WebP");int32(f,2);text(f,L"png");text(f,L"jpg");
        text(f,L"wav");text(f,L"Audio/FLAC");int32(f,1);text(f,L"wav");
    }
    void writeFormats(){
        wchar_t exe[32768];GetModuleFileNameW(nullptr,exe,32768);
        std::ofstream f(file,std::ios::binary);f.write("FCM1",4);text(f,exe);text(f,L"Configure");int32(f,0);int32(f,7);
        for(const auto& format:{L"gif",L"webp",L"jpg",L"png",L"ico",L"avif",L"pdf"}){
            text(f,std::wstring(L"format.")+format);text(f,std::wstring(L"Images/")+format);int32(f,1);text(f,L"png");
        }
    }
};
struct TestRootGuard{
    std::filesystem::path root;
    TestRootGuard(){
        wchar_t temp[32768],id[40];GUID guid;
        require(GetTempPathW(32768,temp)!=0&&SUCCEEDED(CoCreateGuid(&guid)),"isolated test directory");
        StringFromGUID2(guid,id,40);root=std::filesystem::path(temp)/(std::wstring(L"FileConverter-ShellHost-")+id);
        std::filesystem::create_directories(root);
        require(SetEnvironmentVariableW(L"FILECONVERTER_SHELL_TEST_ROOT",root.c_str())!=0,"test environment");
    }
    ~TestRootGuard(){SetEnvironmentVariableW(L"FILECONVERTER_SHELL_TEST_ROOT",nullptr);std::error_code error;std::filesystem::remove_all(root,error);}
};
class Data final:public IDataObject{
    std::atomic<ULONG> refs{1};std::vector<std::wstring> paths;
public:
    explicit Data(std::vector<std::wstring> p):paths(std::move(p)){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** p)override{if(!p)return E_POINTER;*p=nullptr;if(id!=IID_IUnknown&&id!=IID_IDataObject)return E_NOINTERFACE;*p=this;AddRef();return S_OK;}
    ULONG STDMETHODCALLTYPE AddRef()override{return ++refs;}
    ULONG STDMETHODCALLTYPE Release()override{auto n=--refs;if(!n)delete this;return n;}
    HRESULT STDMETHODCALLTYPE GetData(FORMATETC* format,STGMEDIUM* medium)override{
        if(!format||!medium||format->cfFormat!=CF_HDROP)return DV_E_FORMATETC;
        size_t count=1;for(auto& p:paths)count+=p.size()+1;
        auto memory=GlobalAlloc(GHND,sizeof(DROPFILES)+count*sizeof(wchar_t));if(!memory)return E_OUTOFMEMORY;
        auto drop=(DROPFILES*)GlobalLock(memory);drop->pFiles=sizeof(DROPFILES);drop->fWide=TRUE;
        auto cursor=(wchar_t*)((char*)drop+sizeof(DROPFILES));
        for(auto& p:paths){memcpy(cursor,p.c_str(),(p.size()+1)*sizeof(wchar_t));cursor+=p.size()+1;}
        GlobalUnlock(memory);medium->tymed=TYMED_HGLOBAL;medium->hGlobal=memory;medium->pUnkForRelease=nullptr;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetDataHere(FORMATETC*,STGMEDIUM*)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE QueryGetData(FORMATETC*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE GetCanonicalFormatEtc(FORMATETC*,FORMATETC*)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetData(FORMATETC*,STGMEDIUM*,BOOL)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnumFormatEtc(DWORD,IEnumFORMATETC**)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE DAdvise(FORMATETC*,DWORD,IAdviseSink*,DWORD*)override{return OLE_E_ADVISENOTSUPPORTED;}
    HRESULT STDMETHODCALLTYPE DUnadvise(DWORD)override{return OLE_E_ADVISENOTSUPPORTED;}
    HRESULT STDMETHODCALLTYPE EnumDAdvise(IEnumSTATDATA**)override{return OLE_E_ADVISENOTSUPPORTED;}
};
static void waitCapture(const std::string& expectedPreset){
    auto directory=appData()/L"Requests";bool found=false;
    for(int i=0;i<100;i++){
        if(std::filesystem::exists(directory))for(auto& file:std::filesystem::directory_iterator(directory)){
            if(file.path().extension()==L".captured"){
                auto request=file.path();request.replace_extension(L"");
                std::ifstream input(request,std::ios::binary);std::string json(std::istreambuf_iterator<char>(input),{});input.close();
                std::filesystem::remove(file.path());std::filesystem::remove(request);found=true;
                require(json.find("\"Preset\":\""+expectedPreset+"\"")!=std::string::npos,"clicked menu invoked a different preset");
            }
        }
        if(found)break;Sleep(50);
    }
    require(found,"invocation did not reach isolated process");
}
int wmain(int argc,wchar_t** argv){
    if(argc==3&&std::wstring(argv[1])==L"--request"){std::ofstream capture(std::wstring(argv[2])+L".captured");capture<<"ok";return 0;}
    if(argc!=3&&argc!=4){std::cerr<<"ShellHost <dll> <aot|cpp> [previous-aot-dll]\n";return 2;}
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    try{
        TestRootGuard isolated;
        SnapshotGuard snapshot;snapshot.write();
        if(argc==4){
            auto previous=LoadLibraryW(argv[3]);require(previous!=nullptr,"load previously pinned AOT");
            auto previousGet=(HRESULT(__stdcall*)(REFCLSID,REFIID,void**))GetProcAddress(previous,"DllGetClassObject");
            CLSID previousId;CLSIDFromString(L"{61699489-9EEA-43D6-BF5B-32DB32B7A101}",&previousId);
            IClassFactory* previousFactory=nullptr;
            require(previousGet&&SUCCEEDED(previousGet(previousId,IID_IClassFactory,(void**)&previousFactory)),"pin previously loaded AOT");
            previousFactory->Release();
        }
        auto dll=LoadLibraryW(argv[1]);require(dll!=nullptr,"LoadLibrary failed");
        auto get=(HRESULT(__stdcall*)(REFCLSID,REFIID,void**))GetProcAddress(dll,"DllGetClassObject");
        auto unload=(HRESULT(__stdcall*)())GetProcAddress(dll,"DllCanUnloadNow");
        require(get&&unload,"missing exports");
        CLSID id;CLSIDFromString(std::wstring(argv[2])==L"aot"?L"{61699489-9EEA-43D6-BF5B-32DB32B7A201}":L"{61699489-9EEA-43D6-BF5B-32DB32B7A202}",&id);
        IClassFactory* factory=nullptr;require(SUCCEEDED(get(id,IID_IClassFactory,(void**)&factory)),"factory");
        IShellExtInit* init=nullptr;require(SUCCEEDED(factory->CreateInstance(nullptr,IID_IShellExtInit,(void**)&init)),"create");
        IContextMenu* menu=nullptr;require(SUCCEEDED(init->QueryInterface(IID_IContextMenu,(void**)&menu)),"menu interface");
        auto data=new Data({L"C:\\中文 路径\\a.png",L"C:\\中文 路径\\b.jpg"});
        require(SUCCEEDED(init->Initialize(nullptr,data,nullptr)),"initialize");data->Release();
        for(int i=0;i<20;i++){
            HMENU parent=CreatePopupMenu();auto hr=menu->QueryContextMenu(parent,0,100,200,0);
            require(SUCCEEDED(hr)&&HRESULT_CODE(hr)==3,"command count");
            require(GetMenuState(parent,100,MF_BYCOMMAND)&MF_GRAYED,"mixed preset must be disabled");
            DestroyMenu(parent);
        }
        HMENU parent=CreatePopupMenu();
        require(HRESULT_CODE(menu->QueryContextMenu(parent,0,100,100,0))==0,"command ID boundary");DestroyMenu(parent);
        parent=CreatePopupMenu();require(HRESULT_CODE(menu->QueryContextMenu(parent,0,100,200,CMF_DEFAULTONLY))==0,"default-only");
        require(HRESULT_CODE(menu->QueryContextMenu(parent,0,100,200,0))==3,"refresh");
        wchar_t verb[100]{};require(SUCCEEDED(menu->GetCommandString(1,GCS_VERBW,nullptr,(char*)verb,100)),"unicode canonical");
        require(std::wstring(verb)==L"fileconverter.webp","canonical mismatch");
        char ansi[100]{};require(SUCCEEDED(menu->GetCommandString(1,GCS_VERBA,nullptr,ansi,100)),"ansi canonical");
        wchar_t helpWide[100]{};char helpAnsi[100]{},expectedAnsi[100]{};
        require(SUCCEEDED(menu->GetCommandString(0,GCS_HELPTEXTW,nullptr,(char*)helpWide,100)),"Unicode help text");
        require(std::wstring(helpWide)==L"Images/\x56fe\x7247PNG","localized help text");
        WideCharToMultiByte(CP_ACP,0,helpWide,-1,expectedAnsi,100,nullptr,nullptr);
        require(SUCCEEDED(menu->GetCommandString(0,GCS_HELPTEXTA,nullptr,helpAnsi,100)),"ANSI help text");
        require(std::string(helpAnsi)==expectedAnsi,"ANSI help must use Windows code page");
        CMINVOKECOMMANDINFO numeric{sizeof(numeric)};numeric.lpVerb=MAKEINTRESOURCEA(0);
        require(FAILED(menu->InvokeCommand(&numeric)),"disabled command invoked");
        numeric.lpVerb=MAKEINTRESOURCEA(65535);require(FAILED(menu->InvokeCommand(&numeric)),"invalid numeric verb");
        numeric.cbSize=1;require(FAILED(menu->InvokeCommand(&numeric)),"short structure");
        CMINVOKECOMMANDINFOEX wide{};wide.cbSize=sizeof(wide);wide.fMask=CMIC_MASK_UNICODE;wide.lpVerb="unknown";wide.lpVerbW=L"unknown";
        require(FAILED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"unknown string verb");
        require((UINT_PTR)verb>0xffffffffULL,"test must exercise pointer wider than 32 bits");
        wide.lpVerb=nullptr;wide.lpVerbW=verb;require(SUCCEEDED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"64-bit unicode pointer invocation");waitCapture("webp");
        numeric.cbSize=sizeof(numeric);numeric.lpVerb=ansi;
        require((UINT_PTR)ansi>0xffffffffULL,"ANSI pointer width");
        require(SUCCEEDED(menu->InvokeCommand(&numeric)),"ANSI pointer invocation");waitCapture("webp");
        numeric.lpVerb=MAKEINTRESOURCEA(1);require(SUCCEEDED(menu->InvokeCommand(&numeric)),"numeric invocation");waitCapture("webp");
        DestroyMenu(parent);
        snapshot.writeFormats();
        data=new Data({L"C:\\中文 路径\\a.png"});require(SUCCEEDED(init->Initialize(nullptr,data,nullptr)),"format initialize");data->Release();
        parent=CreatePopupMenu();require(HRESULT_CODE(menu->QueryContextMenu(parent,0,173,200,0))==8,"seven image formats and settings");
        UINT offset=0;
        for(const auto& format:{"gif","webp","jpg","png","ico","avif","pdf"}){
            char actual[100]{};
            require(SUCCEEDED(menu->GetCommandString(offset,GCS_VERBA,nullptr,actual,100)),"format canonical");
            auto expected=std::string("format.")+format;
            require(std::string(actual)=="fileconverter."+expected,"format order mismatch");
            CMINVOKECOMMANDINFOEX click{};click.cbSize=sizeof(click);click.fMask=CMIC_MASK_UNICODE;click.lpVerb=MAKEINTRESOURCEA(offset);
            require(SUCCEEDED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&click)),"format click");waitCapture(expected);
            numeric.lpVerb=MAKEINTRESOURCEA(offset);
            require(SUCCEEDED(menu->InvokeCommand(&numeric)),"ANSI numeric format click");waitCapture(expected);
            offset++;
        }
        DestroyMenu(parent);
        // Explorer sets CMIC_MASK_UNICODE for numeric commands too. lpVerbW may be null.
        snapshot.write();
        data=new Data({L"C:\\中文 路径\\a.png"});require(SUCCEEDED(init->Initialize(nullptr,data,nullptr)),"single initialize");data->Release();
        parent=CreatePopupMenu();require(HRESULT_CODE(menu->QueryContextMenu(parent,0,100,200,0))==3,"single refresh");
        wide.lpVerb=MAKEINTRESOURCEA(1);wide.lpVerbW=nullptr;
        require(SUCCEEDED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"Unicode flag with numeric verb");waitCapture("webp");
        wide.lpVerbW=MAKEINTRESOURCEW(65535);
        require(SUCCEEDED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"numeric offset must come from lpVerb rather than lpVerbW");waitCapture("webp");
        wide.lpVerb=MAKEINTRESOURCEA(65535);wide.lpVerbW=nullptr;
        require(FAILED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"Unicode flag must not turn invalid numeric verb into first preset");
        wide.lpVerb="fileconverter.webp";wide.lpVerbW=nullptr;
        require(FAILED(menu->InvokeCommand((CMINVOKECOMMANDINFO*)&wide)),"null Unicode string verb must not invoke first preset");
        DestroyMenu(parent);
        // Corrupt configuration must fail without crashing the host.
        {std::ofstream bad(snapshot.file,std::ios::binary);bad<<"broken";}
        data=new Data({L"C:\\a.png"});require(FAILED(init->Initialize(nullptr,data,nullptr)),"bad snapshot must fail");data->Release();
        menu->Release();init->Release();factory->Release();
        if(std::wstring(argv[2])==L"cpp"){require(unload()==S_OK,"C++ unload reference leak");FreeLibrary(dll);}
        else require(unload()==S_FALSE,"AOT must never advertise unload");
        std::cout<<"PASS: COM lifecycle, 20 repeated menus, mixed inputs, command boundaries, 64-bit Unicode/ANSI verbs, numeric verbs with Unicode flag, all seven image format request IDs, corrupt configuration.\n";
        CoUninitialize();return 0;
    }catch(const std::exception& error){std::cerr<<"FAIL: "<<error.what()<<" Win32="<<GetLastError()<<"\n";CoUninitialize();return 1;}
}
