import Foundation
import CryptoKit
import AppKit

struct ReleaseVersion: Comparable, CustomStringConvertible {
    let major: Int, minor: Int, patch: Int
    init?(_ text: String) {
        let normalized = text.hasPrefix("v") ? String(text.dropFirst()) : text
        let parts = normalized.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 3, parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy({ $0.isASCII && $0.isNumber }) && ($0 == "0" || !$0.hasPrefix("0")) }),
              let a = Int(parts[0]), let b = Int(parts[1]), let c = Int(parts[2]) else { return nil }
        major = a; minor = b; patch = c
    }
    var description: String { "\(major).\(minor).\(patch)" }
    static func < (a: Self, b: Self) -> Bool { (a.major, a.minor, a.patch) < (b.major, b.minor, b.patch) }
}

struct ReleaseUpdate {
    let version: ReleaseVersion
    let url: URL
    let digest: String
    let size: Int64
}

final class UpdateRedirects: NSObject, URLSessionTaskDelegate {
    static func allowed(_ url: URL) -> Bool {
        url.scheme == "https" && url.user == nil && url.password == nil && (url.port == nil || url.port == 443) &&
        ["api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"].contains(url.host ?? "")
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(request.url.map(Self.allowed) == true ? request : nil)
    }
}

enum Updates {
    static let repository = "b0rdik/EdgeLink"
    static let asset = "EdgeLink-macos-arm64.zip"
    static var current: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.3.1" }
    static func parse(_ data: Data, current: String) throws -> ReleaseUpdate? {
        struct Asset: Decodable { let name: String; let browser_download_url: String; let digest: String?; let size: Int64 }
        struct Release: Decodable { let tag_name: String; let draft: Bool; let prerelease: Bool; let assets: [Asset] }
        let release = try JSONDecoder().decode(Release.self, from: data)
        if release.draft || release.prerelease { return nil }
        guard let version = ReleaseVersion(release.tag_name), let installed = ReleaseVersion(current) else { throw LinkError.message("Неподдерживаемый номер версии GitHub Release") }
        if version <= installed { return nil }
        let matches = release.assets.filter { $0.name == asset }
        guard matches.count == 1, let file = matches.first else { throw LinkError.message("В релизе отсутствует сборка для Mac") }
        let expected = "https://github.com/\(repository)/releases/download/\(release.tag_name)/\(asset)"
        guard file.browser_download_url == expected, let url = URL(string: expected) else { throw LinkError.message("Неожиданный адрес файла обновления") }
        guard let rawDigest = file.digest, rawDigest.hasPrefix("sha256:") else { throw LinkError.message("GitHub не предоставил SHA-256; установка отменена") }
        let digest = String(rawDigest.dropFirst(7)).lowercased()
        guard digest.count == 64 && digest.allSatisfy({ "0123456789abcdef".contains($0) }), file.size > 0 && file.size <= 250 * 1024 * 1024 else { throw LinkError.message("Неверные параметры обновления") }
        return ReleaseUpdate(version: version, url: url, digest: digest, size: file.size)
    }
    static func session() -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15; config.timeoutIntervalForResource = 300
        config.httpAdditionalHeaders = ["User-Agent": "EdgeLink/\(current)"]
        return URLSession(configuration: config, delegate: UpdateRedirects(), delegateQueue: nil)
    }
    static func check() async throws -> ReleaseUpdate? {
        let client = session(); defer { client.finishTasksAndInvalidate() }
        var request = URLRequest(url: URL(string: "https://api.github.com/repos/\(repository)/releases/latest")!)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        let (data, response) = try await client.data(for: request)
        guard let response = response as? HTTPURLResponse, response.statusCode == 200 else { throw LinkError.message("Репозиторий или опубликованный релиз пока недоступен") }
        guard data.count <= 1024 * 1024 else { throw LinkError.message("Metadata too large") }
        return try parse(data, current: current)
    }
    static func stage(_ update: ReleaseUpdate) async throws -> URL {
        let fm = FileManager.default
        let folder = fm.temporaryDirectory.appendingPathComponent("EdgeLink-update-" + UUID().uuidString, isDirectory: true)
        try fm.createDirectory(at: folder, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        do {
            let client = session(); defer { client.finishTasksAndInvalidate() }
            let (temporary, response) = try await client.download(from: update.url)
            guard let response = response as? HTTPURLResponse, response.statusCode == 200 else { throw LinkError.message("Не удалось скачать обновление") }
            let size = (try fm.attributesOfItem(atPath: temporary.path)[.size] as? NSNumber)?.int64Value
            guard size == update.size else { throw LinkError.message("Загрузка не завершена или размер не совпадает") }
            let file = try FileHandle(forReadingFrom: temporary); defer { try? file.close() }
            var hash = SHA256()
            while let block = try file.read(upToCount: 65536), !block.isEmpty { hash.update(data: block) }
            let actual = hash.finalize().map { String(format: "%02x", $0) }.joined()
            guard actual == update.digest else { throw LinkError.message("SHA-256 обновления не совпадает") }
            let zip = folder.appendingPathComponent(asset); try fm.moveItem(at: temporary, to: zip)
            let list = try run("/usr/bin/zipinfo", ["-1", zip.path]).split(separator: "\n", omittingEmptySubsequences: true)
            guard !list.isEmpty, list.allSatisfy({ safeEntry(String($0)) }) else { throw LinkError.message("Небезопасные пути в ZIP") }
            let details = try run("/usr/bin/zipinfo", ["-l", zip.path])
            guard !details.split(separator: "\n").contains(where: { $0.hasPrefix("l") }) else { throw LinkError.message("Символические ссылки в обновлении запрещены") }
            let unpack = folder.appendingPathComponent("unpacked", isDirectory: true); try fm.createDirectory(at: unpack, withIntermediateDirectories: true)
            _ = try run("/usr/bin/ditto", ["-x", "-k", zip.path, unpack.path])
            let app = unpack.appendingPathComponent("EdgeLink.app", isDirectory: true)
            let infoURL = app.appendingPathComponent("Contents/Info.plist")
            let info = try PropertyListSerialization.propertyList(from: Data(contentsOf: infoURL), format: nil) as? [String: Any]
            guard info?["CFBundleIdentifier"] as? String == "local.edgelink.mac", info?["CFBundleExecutable"] as? String == "EdgeLink",
                  info?["CFBundleShortVersionString"] as? String == update.version.description else { throw LinkError.message("Версия или идентификатор приложения не совпадает") }
            let binary = try FileHandle(forReadingFrom: app.appendingPathComponent("Contents/MacOS/EdgeLink")); defer { try? binary.close() }
            let header = try binary.read(upToCount: 8) ?? Data()
            guard header.count == 8, header.little(0, as: UInt32.self) == 0xfeedfacf,
                  header.little(4, as: UInt32.self) == 0x0100000c else { throw LinkError.message("Обновление не для Apple Silicon") }
            _ = try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
            return app
        } catch { try? fm.removeItem(at: folder); throw error }
    }
    static func safeEntry(_ path: String) -> Bool {
        !path.hasPrefix("/") && !path.contains("\\") && !path.contains("\r") && !path.contains("\0") &&
        !path.split(separator: "/", omittingEmptySubsequences: false).contains("..") &&
        (path.hasPrefix("EdgeLink.app/") || path.hasPrefix("__MACOSX/EdgeLink.app/") || path == "__MACOSX/" || path == "EdgeLink.app/")
    }
    static func run(_ path: String, _ args: [String]) throws -> String {
        let process = Process(), pipe = Pipe()
        process.executableURL = URL(fileURLWithPath: path); process.arguments = args
        process.standardOutput = pipe; process.standardError = pipe
        try process.run(); let data = pipe.fileHandleForReading.readDataToEndOfFile(); process.waitUntilExit()
        let text = String(data: data, encoding: .utf8) ?? ""
        guard process.terminationStatus == 0 else { throw LinkError.message(text.isEmpty ? "Ошибка подготовки обновления" : String(text.prefix(500))) }
        return text
    }
    static func installer(_ staged: URL, target: URL = Bundle.main.bundleURL) throws -> Process {
        let fm = FileManager.default
        guard target.pathExtension == "app", !target.path.contains("/AppTranslocation/"), fm.isWritableFile(atPath: target.deletingLastPathComponent().path) else {
            throw LinkError.message("Переместите EdgeLink в доступную для записи папку Applications и запустите оттуда")
        }
        let folder = staged.deletingLastPathComponent().deletingLastPathComponent()
        let script = folder.appendingPathComponent("install.sh")
        try installerScript.write(to: script, atomically: true, encoding: .utf8)
        let nonce = UUID().uuidString
        let ready = folder.appendingPathComponent("ready-" + nonce + ".txt")
        let log = fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/EdgeLink-update.log")
        let process = Process(); process.executableURL = URL(fileURLWithPath: "/bin/bash")
        process.arguments = [script.path, String(getpid()), staged.path, target.path, log.path, ready.path, nonce, folder.path]
        try process.run(); return process
    }
    static func replaceBundle(_ incoming: URL, target: URL) throws {
        guard incoming.pathExtension == "app", target.pathExtension == "app", incoming.standardizedFileURL != target.standardizedFileURL else {
            throw LinkError.message("Неверные пути замены приложения")
        }
        // FileManager commits the replacement without a named backup item.
        _ = try FileManager.default.replaceItemAt(target, withItemAt: incoming, backupItemName: nil, options: .usingNewMetadataOnly)
    }
    static func reportReady(_ arguments: [String] = CommandLine.arguments) {
        guard let index = arguments.firstIndex(of: "--update-ready"), arguments.count > index + 2,
              UUID(uuidString: arguments[index + 2]) != nil else { return }
        let nonce = arguments[index + 2], url = URL(fileURLWithPath: arguments[index + 1]).standardizedFileURL
        let folder = url.deletingLastPathComponent()
        guard url.lastPathComponent == "ready-" + nonce + ".txt",
              folder.lastPathComponent.hasPrefix("EdgeLink-update-"),
              UUID(uuidString: String(folder.lastPathComponent.dropFirst("EdgeLink-update-".count))) != nil,
              folder.deletingLastPathComponent().resolvingSymlinksInPath() == FileManager.default.temporaryDirectory.resolvingSymlinksInPath() else { return }
        try? Data(nonce.utf8).write(to: url, options: .atomic)
    }
    @discardableResult
    static func removeLegacyBackups(target: URL = Bundle.main.bundleURL) -> Int {
        let fm = FileManager.default, parent = target.deletingLastPathComponent()
        let prefix = target.deletingPathExtension().lastPathComponent + ".previous-"
        var removed = 0
        for candidate in (try? fm.contentsOfDirectory(at: parent, includingPropertiesForKeys: [.isSymbolicLinkKey, .isDirectoryKey])) ?? [] {
            let name = candidate.lastPathComponent
            guard name.hasPrefix(prefix), name.hasSuffix(".app"),
                  UUID(uuidString: String(name.dropFirst(prefix.count).dropLast(4))) != nil,
                  let values = try? candidate.resourceValues(forKeys: [.isSymbolicLinkKey, .isDirectoryKey]),
                  values.isDirectory == true, values.isSymbolicLink != true,
                  Bundle(url: candidate)?.bundleIdentifier == "local.edgelink.mac" else { continue }
            do { try fm.removeItem(at: candidate); removed += 1 }
            catch { NSLog("EdgeLink: previous update copy could not be removed: %@", error.localizedDescription) }
        }
        return removed
    }
    static let installerScript = #"""
#!/bin/bash
set -eu
parent="$1"; staged="$2"; target="$3"; log="$4"; ready="$5"; nonce="$6"; folder="$7"
incoming="${target}.incoming-$$.app"
committed=0
child=""
finish() {
  code=$?
  if [ "$code" -ne 0 ]; then
    if [ -n "$child" ]; then
      /bin/kill "$child" 2>/dev/null || true
      for i in $(/usr/bin/seq 1 20); do /bin/kill -0 "$child" 2>/dev/null || break; /bin/sleep 0.1; done
      /bin/kill -9 "$child" 2>/dev/null || true
      wait "$child" 2>/dev/null || true
    fi
    if [ "$committed" -eq 0 ]; then /usr/bin/open "$target" || true; fi
  fi
  if [ -d "$incoming" ]; then /bin/rm -rf "$incoming"; fi
  /bin/rm -rf "$folder"
  exit "$code"
}
exec >> "$log" 2>&1
trap finish EXIT
for i in $(/usr/bin/seq 1 300); do
  if ! /bin/kill -0 "$parent" 2>/dev/null; then break; fi
  /bin/sleep 0.1
done
if /bin/kill -0 "$parent" 2>/dev/null; then echo 'Application did not exit'; exit 1; fi
/usr/bin/ditto "$staged" "$incoming"
/usr/bin/codesign --verify --deep --strict "$incoming"
"$target/Contents/MacOS/EdgeLink" --replace-bundle "$incoming" "$target"
committed=1
"$target/Contents/MacOS/EdgeLink" --update-ready "$ready" "$nonce" &
child=$!
confirmed=0
for i in $(/usr/bin/seq 1 300); do
  if [ -f "$ready" ] && [ "$(/bin/cat "$ready")" = "$nonce" ]; then confirmed=1; break; fi
  if ! /bin/kill -0 "$child" 2>/dev/null; then echo 'New application exited before confirming startup'; exit 1; fi
  /bin/sleep 0.1
done
if [ "$confirmed" -ne 1 ]; then echo 'New application did not confirm startup'; exit 1; fi
echo 'Update installed successfully'
"""#
}
