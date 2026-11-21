using System.Collections;
using UnityEngine;

public class PokeBallThrow : MonoBehaviour
{
    [Header("起點與目標辨識")]
    public Transform startPoint;           // 精靈球起點（可留空，預設為初始位置）
    public string pokemonTag = "Pokemon";  // 寶可夢 Tag
    public Transform targetPokemon;        // 可選：指定寶可夢 Transform

    [Header("平面拋物線參數（固定 Z 軸與固定球速）")]
    public float throwSpeed = 14f;         // 固定球速（單位/秒），越快數值越大
    public float arcHeight = 2.2f;         // 拋物線最高點高度
    public float minTargetX = -15f;        // 落點 X 最小限制
    public float maxTargetX = 15f;         // 落點 X 最大限制
    public float fallbackDistance = 10f;   // 點到空白處時的預設水平位移
    public LayerMask aimLayerMask = ~0;    // 滑鼠射線可命中的圖層

    [Header("抓捕特效、音效與球落地")]
    public ParticleSystem captureEffect;   // 收服時產生的特效 Prefab
    public AudioSource audioSource;        // 播放音效用（可留空，會自動抓或補上）
    public AudioClip captureSound;         // 抓捕音效檔（.wav / .mp3 / .ogg）
    public float groundY = 0f;             // 球落地的 Y 高度
    public float dropDuration = 0.45f;     // 抓捕後球落地的時間（秒）
    public float resetDelay = 2.0f;        // 沒抓到時幾秒後回起點（設 0 不自動回位）

    private float _fixedZ;
    private Vector3 _startPos;
    private Vector3 _endPos;
    private float _currentDuration = 1f;
    private float _timer = 0f;
    private bool _isThrowing = false;
    private bool _isCaptured = false;

    void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }
    }

    void Start()
    {
        Vector3 initPos = startPoint != null ? startPoint.position : transform.position;
        _fixedZ = initPos.z;
        _startPos = new Vector3(initPos.x, initPos.y, _fixedZ);
        ResetBallToStart();
    }

    void Update()
    {
        if (_isCaptured) return;

        if (!_isThrowing && Input.GetMouseButtonDown(0))
        {
            Vector3 initPos = startPoint != null ? startPoint.position : _startPos;
            _fixedZ = initPos.z;
            _startPos = new Vector3(initPos.x, initPos.y, _fixedZ);

            if (TryGetPlaneAimPoint(out Vector3 aimPoint))
            {
                transform.position = _startPos;
                _endPos = aimPoint;

                // 依起點與落點距離計算飛行時間，保持球速一致
                float dist = Vector2.Distance(
                    new Vector2(_startPos.x, _startPos.y),
                    new Vector2(_endPos.x, _endPos.y)
                );
                _currentDuration = Mathf.Max(0.15f, dist / Mathf.Max(0.1f, throwSpeed));
                _timer = 0f;
                _isThrowing = true;
            }
        }

        if (_isThrowing)
        {
            _timer += Time.deltaTime;
            float t = Mathf.Clamp01(_timer / _currentDuration);

            Vector3 pos = Vector3.Lerp(_startPos, _endPos, t);
            pos.z = _fixedZ;
            pos.y += 4f * arcHeight * t * (1f - t);

            transform.position = pos;
            transform.Rotate(Vector3.forward * 720f * Time.deltaTime);

            if (t >= 1f)
            {
                _isThrowing = false;
                if (resetDelay > 0f)
                {
                    Invoke(nameof(ResetBallToStart), resetDelay);
                }
            }
        }
    }

    private bool TryGetPlaneAimPoint(out Vector3 point)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            float x = Mathf.Clamp(_startPos.x + fallbackDistance, minTargetX, maxTargetX);
            point = new Vector3(x, _startPos.y, _fixedZ);
            return true;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 rawPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, 200f, aimLayerMask))
        {
            rawPoint = hit.point;
        }
        else
        {
            float denom = ray.direction.z;
            if (Mathf.Abs(denom) > 0.0001f)
            {
                float enter = (_fixedZ - ray.origin.z) / denom;
                rawPoint = enter > 0f ? ray.GetPoint(enter) : ray.GetPoint(fallbackDistance);
            }
            else
            {
                rawPoint = ray.GetPoint(fallbackDistance);
            }
        }

        float clampedX = Mathf.Clamp(rawPoint.x, minTargetX, maxTargetX);
        float targetY = Mathf.Max(rawPoint.y, _startPos.y - 1f);
        point = new Vector3(clampedX, targetY, _fixedZ);
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCaptureOther(other.transform);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryCaptureOther(collision.transform);
    }

    private void TryCaptureOther(Transform hitTransform)
    {
        if (_isCaptured || !_isThrowing) return;

        bool isPokemon = false;
        if (!string.IsNullOrEmpty(pokemonTag) && hitTransform.CompareTag(pokemonTag))
        {
            isPokemon = true;
        }
        else if (targetPokemon != null && (hitTransform == targetPokemon || hitTransform.IsChildOf(targetPokemon)))
        {
            isPokemon = true;
        }

        if (isPokemon)
        {
            CancelInvoke(nameof(ResetBallToStart));
            _isThrowing = false;
            _isCaptured = true;

            if (captureSound != null && audioSource != null)
            {
                audioSource.PlayOneShot(captureSound);
            }

            Vector3 effectPos = new Vector3(hitTransform.position.x, hitTransform.position.y + 0.8f, _fixedZ);
            if (captureEffect != null)
            {
                ParticleSystem fx = Instantiate(captureEffect, effectPos, Quaternion.identity);
                fx.Play();
                Destroy(fx.gameObject, 3f);
            }

            Transform rootToHide = targetPokemon != null ? targetPokemon : hitTransform;
            StartCoroutine(ShrinkAndHide(rootToHide));
            StartCoroutine(DropBallToGround());
        }
    }

    private IEnumerator ShrinkAndHide(Transform target)
    {
        Vector3 originScale = target.localScale;
        float duration = 0.25f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            target.localScale = Vector3.Lerp(originScale, Vector3.zero, k);
            yield return null;
        }

        target.gameObject.SetActive(false);
        target.localScale = originScale;
    }

    private IEnumerator DropBallToGround()
    {
        Vector3 dropStart = new Vector3(transform.position.x, transform.position.y, _fixedZ);
        float ballRadius = GetComponent<SphereCollider>() != null
            ? GetComponent<SphereCollider>().radius * Mathf.Abs(transform.lossyScale.y)
            : 0.25f;
        float targetY = Mathf.Min(dropStart.y, groundY + ballRadius);
        Vector3 dropEnd = new Vector3(dropStart.x, targetY, _fixedZ);

        float elapsed = 0f;
        while (elapsed < dropDuration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / dropDuration);
            float eased = k * k;
            transform.position = Vector3.Lerp(dropStart, dropEnd, eased);
            transform.Rotate(Vector3.forward * 360f * Time.deltaTime);
            yield return null;
        }

        transform.position = dropEnd;
    }

    private void ResetBallToStart()
    {
        if (_isCaptured) return;
        _isThrowing = false;
        transform.position = _startPos;
    }
}