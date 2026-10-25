# Unity Mesh Tools

Unity 2022.3 LTS와 Unity 6에서 사용하는 공용 편집기 패키지입니다.

## 설치

GitHub private 저장소에 접근할 수 있는 계정의 Git 인증을 먼저 준비합니다.
인증 토큰을 설치 URL이나 프로젝트 파일에 넣지 않습니다.
Ragdoll Helper 1.6.1은 프로젝트에 별도로 설치합니다.
Unity Package Manager에서 다음 Git URL을 설치합니다.

```text
https://github.com/iruril/unity-mesh-tools.git?path=/Packages/com.mesh-tools.skinned-mesh-cutter#v1.1.0
```

| Unity | 프로젝트의 ProBuilder 버전 |
| --- | --- |
| 2022.3 LTS | 5.2.4 |
| 6.3 LTS | 6.1.2 |

절단 패키지는 ProBuilder 5.2.4를 최소 의존성으로 선언합니다. Unity 6 프로젝트에서는
ProBuilder 6.1.2를 직접 설치하여 유지합니다. Unity API 이름 차이는 소스의 버전 분기로 처리합니다.
래그돌 자동 생성에는 프로젝트에 별도로 설치한 Ragdoll Helper가 필요합니다.

## 구성

- `Packages/com.mesh-tools.skinned-mesh-cutter`: 절단, 열린 단면 캡, UV, 마스크 편집, 래그돌 출력.
- 절단 패키지의 `Tests/Editor`: 프로젝트 전용 모델에 의존하지 않는 회귀 테스트.

## 수정과 배포

이 저장소의 소스를 수정하고 두 Unity 버전에서 검증한 뒤 새 태그를 만듭니다.
기존 릴리스 태그는 이동하지 않습니다. 각 프로젝트에서 새 태그 URL을 설치하고
`Packages/manifest.json`과 `Packages/packages-lock.json`을 함께 커밋합니다.
설치된 `Library/PackageCache` 사본을 직접 수정하지 않습니다.

TheIRONY의 편집 창 확장과 게임 전용 설정은 각 프로젝트에 유지합니다.
프로젝트별 씬, 캐릭터 모델, 생성된 메시 및 재질은 이 저장소에 포함하지 않습니다.
