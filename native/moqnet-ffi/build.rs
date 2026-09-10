use std::env;
use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-changed=src");
    println!("cargo:rerun-if-changed=build.rs");

    let crate_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
    let output_dir = PathBuf::from(&crate_dir).join("../output/generated");
    std::fs::create_dir_all(&output_dir).unwrap();

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/ffi.rs")
        .input_extern_file("src/client.rs")
        .input_extern_file("src/origin.rs")
        .input_extern_file("src/producer.rs")
        .input_extern_file("src/consumer.rs")
        .input_extern_file("src/json.rs")
        .input_extern_file("src/server.rs")
        .input_extern_file("src/audio.rs")
        .input_extern_file("src/video.rs")
        .csharp_dll_name("moqnet_ffi")
        .csharp_dll_name_if("(UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR", "__Internal")
        .csharp_class_name("NativeMethods")
        .csharp_namespace("MoqNet.Native.FFI")
        .csharp_use_function_pointer(false)
        .generate_csharp_file(output_dir.join("NativeMethods.g.cs"))
        .expect("csbindgen failed");

    println!("cargo:warning=C# bindings generated at {:?}", output_dir);
}
