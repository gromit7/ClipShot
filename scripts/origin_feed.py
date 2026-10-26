"""Publish the RF feed from already present, fully verified packages; no network."""
import base64
import contextlib
import datetime
import hashlib
import json
import os
import pathlib
import re
import stat
import sys
import tempfile
import uuid

try:
    import fcntl
except ImportError:  # Windows fixtures replace only the Linux locking primitive.
    fcntl = None

ROOT = pathlib.Path('/var/www/clipshot')
BACKUP_ROOT = pathlib.Path('/root/clipshot-maintenance')
MAX_FEED = 32768
MAX_PACKAGE = 262144000
VERSION = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\Z')
PREFIX = {'legacy': 'ClipShot', 'win10': 'ClipShot-Win10', 'win11': 'ClipShot-Win11'}


def _reject(code):
    raise ValueError(code)


def _object(value, required, optional=()):
    if not isinstance(value, dict) or not set(required) <= value.keys() or value.keys() - set(required) - set(optional):
        _reject('invalid-metadata-fields')


def _version(text):
    if not isinstance(text, str) or not VERSION.fullmatch(text):
        _reject('invalid-version')
    parts = tuple(int(part) for part in text.split('.'))
    if any(part > 2147483647 for part in parts) or parts < (0, 7, 0):
        _reject('invalid-version')
    return parts


def _release(value, flavor, expected=None):
    _object(value, ('draft', 'prerelease', 'tag_name', 'assets'))
    if value['draft'] is not False or value['prerelease'] is not False:
        _reject('non-stable-release')
    tag = value['tag_name']
    if not isinstance(tag, str) or not tag.startswith('v'):
        _reject('invalid-version')
    version = tag[1:]
    _version(version)
    if expected is not None and version != expected:
        _reject('variant-version-mismatch')
    assets = value['assets']
    if not isinstance(assets, list) or len(assets) != 1:
        _reject('one-exact-package-required')
    asset = assets[0]
    _object(asset, ('name', 'size', 'digest', 'browser_download_url'), ('state',))
    name = '{}-Setup-{}.exe'.format(PREFIX[flavor], version)
    if asset['name'] != name or ('state' in asset and asset['state'] != 'uploaded'):
        _reject('package-name-mismatch')
    size, digest = asset['size'], asset['digest']
    if type(size) is not int or not 1024 <= size <= MAX_PACKAGE or not isinstance(digest, str) or not re.fullmatch(r'sha256:[0-9a-fA-F]{64}', digest):
        _reject('invalid-package-size-or-digest')
    allowed = [
        'https://github.com/gromit7/ClipShot/releases/download/{}/{}'.format(tag, name),
        'https://bot.data-clouds.ru/clipshot/{}/{}'.format(tag, name),
        'https://tg.data-clouds.ru/clipshot/{}/{}'.format(tag, name),
    ]
    if asset['browser_download_url'] not in allowed:
        _reject('unexpected-package-url')
    return {'version': version, 'flavor': flavor, 'name': name, 'size': size,
            'sha256': digest[7:].lower(), 'url': allowed[1]}


def _variants(value, version, packages, revoked=False):
    _object(value, ('win10', 'win11'))
    for flavor in ('win10', 'win11'):
        if value[flavor] is not None:
            if revoked:
                _reject('variants-without-stable')
            packages.append(_release(value[flavor], flavor, version))


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            _reject('duplicate-json-key')
        result[key] = value
    return result


def validate_feed(raw):
    if not isinstance(raw, bytes) or not 1 <= len(raw) <= MAX_FEED or raw.startswith(b'\xef\xbb\xbf'):
        _reject('invalid-feed-bytes')
    feed = json.loads(raw.decode('utf-8'), object_pairs_hook=_unique_object,
                      parse_constant=lambda _: _reject('non-json-number'))
    _object(feed, ('schema', 'stable', 'rollback'), ('variants',))
    if type(feed['schema']) is not int or feed['schema'] != 1:
        _reject('unknown-feed-schema')
    packages = []
    stable = feed['stable']
    version = None
    if stable is not None:
        package = _release(stable, 'legacy')
        version = package['version']
        packages.append(package)
    if 'variants' in feed:
        _variants(feed['variants'], version, packages, revoked=stable is None)
    rollback = feed['rollback']
    if rollback is not None:
        _object(rollback, ('policy', 'release'), ('variants',))
        policy = rollback['policy']
        _object(policy, ('schema', 'channel', 'action', 'affectedVersions', 'targetVersion', 'reason'), ('revision',))
        if type(policy['schema']) is not int or policy['schema'] != 1 or policy['channel'] != 'stable' or policy['action'] != 'rollback':
            _reject('invalid-rollback-policy')
        if 'revision' in policy and (type(policy['revision']) is not int or not 1 <= policy['revision'] <= 2147483647):
            _reject('invalid-policy-revision')
        target = _version(policy['targetVersion'])
        affected = policy['affectedVersions']
        if not isinstance(affected, list) or not 1 <= len(affected) <= 256 or any(_version(item) <= target for item in affected) or len(set(affected)) != len(affected):
            _reject('unsafe-rollback-range')
        if not isinstance(policy['reason'], str) or len(policy['reason'].encode('utf-16-le')) // 2 > 300:
            _reject('invalid-rollback-reason')
        packages.append(_release(rollback['release'], 'legacy', policy['targetVersion']))
        if 'variants' in rollback:
            _variants(rollback['variants'], policy['targetVersion'], packages)
    # Одинаковый файл проверяется один раз; противоречивые SHA одного имени запрещены.
    unique = {}
    for package in packages:
        key = (package['version'], package['name'])
        if key in unique and unique[key] != package:
            _reject('conflicting-package-metadata')
        unique[key] = package
    return list(unique.values())


def _directory(path):
    if path.is_symlink() or not path.is_dir():
        _reject('unsafe-directory')


def _fingerprint(info):
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


def _file_identity(info):
    # Windows Python трактует ctime у fd/path по-разному. inode/device и mtime
    # связывают pathname с открытым файлом; ctime внутри fd проверяется отдельно.
    return _fingerprint(info)[:4]


def _read_small(path, optional=False):
    try:
        descriptor = os.open(str(path), os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
    except FileNotFoundError:
        if optional:
            return None
        raise
    with os.fdopen(descriptor, 'rb') as stream:
        before = os.fstat(stream.fileno())
        if path.is_symlink() or not stat.S_ISREG(before.st_mode) or before.st_size > MAX_FEED:
            _reject('unsafe-current-feed')
        raw = stream.read(MAX_FEED + 1)
        if len(raw) > MAX_FEED or _fingerprint(before) != _fingerprint(os.fstat(stream.fileno())):
            _reject('feed-changed-during-read')
        return raw


def _verify_packages(root, packages):
    for package in packages:
        directory = root / ('v' + package['version'])
        _directory(directory)
        path = directory / package['name']
        descriptor = os.open(str(path), os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
        with os.fdopen(descriptor, 'rb') as stream:
            before = os.fstat(stream.fileno())
            if path.is_symlink() or not stat.S_ISREG(before.st_mode) or before.st_size != package['size']:
                _reject('package-file-mismatch')
            digest = hashlib.sha256()
            for block in iter(lambda: stream.read(1048576), b''):
                digest.update(block)
            if digest.hexdigest() != package['sha256'] or _fingerprint(before) != _fingerprint(os.fstat(stream.fileno())) or _file_identity(before) != _file_identity(path.stat()):
                _reject('package-sha256-mismatch')


def _fsync_directory(path):
    descriptor = os.open(str(path), os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


@contextlib.contextmanager
def _publication_lock(root):
    if fcntl is None:
        raise RuntimeError('Linux flock required for publication')
    descriptor = os.open(str(root / '.publish.lock'), os.O_RDWR | os.O_CREAT | getattr(os, 'O_NOFOLLOW', 0), 0o600)
    with os.fdopen(descriptor, 'a+b') as lock:
        if not stat.S_ISREG(os.fstat(lock.fileno()).st_mode):
            _reject('unsafe-publication-lock')
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


def _write_file(descriptor, path, raw, mode):
    with os.fdopen(descriptor, 'wb') as stream:
        stream.write(raw)
        stream.flush()
        if hasattr(os, 'fchmod'):
            os.fchmod(stream.fileno(), mode)
        else:  # Windows fixture: POSIX access controls are verified only on Linux.
            os.chmod(str(path), mode)
        os.fsync(stream.fileno())


def _backup(raw):
    BACKUP_ROOT.mkdir(mode=0o700, parents=True, exist_ok=True)
    _directory(BACKUP_ROOT)
    if os.name == 'posix' and stat.S_IMODE(BACKUP_ROOT.stat().st_mode) & 0o077:
        _reject('maintenance-directory-not-private')
    _fsync_directory(BACKUP_ROOT.parent)
    name = 'origin-feed-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ-') + uuid.uuid4().hex
    directory = BACKUP_ROOT / name
    directory.mkdir(mode=0o700)
    path = directory / 'update-feed.json'
    descriptor = os.open(str(path), os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    _write_file(descriptor, path, raw, 0o600)
    _fsync_directory(directory)
    _fsync_directory(BACKUP_ROOT)
    return name


def publish_origin_feed(root, raw, check=False):
    packages = validate_feed(raw)
    root = pathlib.Path(root).absolute()
    _directory(root)
    target = root / 'update-feed.json'
    wanted = hashlib.sha256(raw).hexdigest()
    backup_id = None
    if check:
        if _read_small(target) != raw:
            _reject('origin-feed-byte-mismatch')
        _verify_packages(root, packages)
        if _read_small(target) != raw:
            _reject('feed-changed-during-check')
    else:
        with _publication_lock(root):
            previous = _read_small(target, optional=True)
            _verify_packages(root, packages)
            if _read_small(target, optional=True) != previous:
                _reject('feed-changed-during-publication')
            # Резервная копия закрыта и находится вне web-root. Старые копии не удаляем.
            if previous is not None:
                backup_id = _backup(previous)
            descriptor, temporary = tempfile.mkstemp(prefix='.origin-feed-', suffix='.tmp', dir=str(root))
            try:
                _write_file(descriptor, temporary, raw, 0o644)
                os.replace(temporary, str(target))
                _fsync_directory(root)
            finally:
                try:
                    os.unlink(temporary)
                except FileNotFoundError:
                    pass
            if _read_small(target) != raw:
                _reject('published-feed-byte-mismatch')
    if os.name == 'posix' and stat.S_IMODE(target.lstat().st_mode) != 0o644:
        _reject('public-feed-mode-not-644')
    return {'verification': 'clipshot-origin-feed-v1', 'mode': 'check' if check else 'publish',
            'feed_sha256': wanted, 'bytes': len(raw), 'current_sha256': wanted,
            'current_matches_input': True, 'packages_count': len(packages),
            'packages': [dict(package, verified=True) for package in packages],
            'atomic_replace': not check, 'backup_id': backup_id}


if __name__ == '__main__':
    try:
        if len(sys.argv) != 4 or sys.argv[1] not in ('publish', 'check') or not re.fullmatch(r'[0-9a-f]{32}', sys.argv[3]):
            _reject('invalid-command')
        receipt = publish_origin_feed(ROOT, base64.b64decode(sys.argv[2], validate=True), sys.argv[1] == 'check')
        receipt['request_id'] = sys.argv[3]
        print(json.dumps(receipt, ensure_ascii=True, separators=(',', ':')))
    except Exception as error:
        # Не выдаём пути ОС, SSH-параметры или произвольное содержимое исключений.
        code = str(error) if type(error) is ValueError and re.fullmatch(r'[a-z0-9-]+', str(error)) else type(error).__name__
        print(json.dumps({'verification': 'clipshot-origin-feed-error-v1', 'error_code': code}))
        sys.exit(1)
